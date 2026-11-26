using MedFlow.Application.Interfaces.Services;
using MedFlow.Application.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedFlow.Infrastructure.BackgroundJobs;

/// <summary>
/// Periodic sweeper (Stage E2 / ADR-0061) recovering <c>queue_enrollment_intents</c> rows left in a
/// non-terminal state by a crashed or timed-out post-commit enrollment run. Delegates to
/// <see cref="IPaymentEnrollmentIntentProcessor.RecoverPendingIntentsAsync"/> which re-runs the
/// idempotent enrollment path under the intent row lock — transient failures get bounded retries,
/// business-ineligible intents settle to <c>SKIPPED_NOT_ELIGIBLE</c> without touching the payment.
/// </summary>
public sealed class PaymentEnrollmentIntentRecoveryProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptionsMonitor<ScheduleJobsOptions> _options;
    private readonly ILogger<PaymentEnrollmentIntentRecoveryProcessor> _logger;

    public PaymentEnrollmentIntentRecoveryProcessor(
        IServiceScopeFactory scopeFactory,
        IOptionsMonitor<ScheduleJobsOptions> options,
        ILogger<PaymentEnrollmentIntentRecoveryProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initialOpts = _options.CurrentValue;
        if (!initialOpts.Enabled)
        {
            _logger.LogInformation(
                "PaymentEnrollmentIntentRecoveryProcessor is disabled via ScheduleJobs:Enabled=false.");
            return;
        }

        // Short sweep cadence: intents are crash-recovery for a synchronous-feeling payment flow, so
        // we poll far more frequently than the 10-minute lifecycle processor.
        var interval = TimeSpan.FromSeconds(Math.Max(15, initialOpts.PaymentIntentRecoveryIntervalSeconds));
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (!_options.CurrentValue.Enabled)
            {
                continue;
            }

            try
            {
                await ProcessCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unhandled error while running PaymentEnrollmentIntentRecoveryProcessor cycle.");
            }
        }
    }

    /// <summary>Runs one recovery sweep; returns the number of intents processed.</summary>
    public async Task<int> ProcessCycleAsync(CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;
        var batchSize = Math.Max(1, opts.BatchSize);

        // The processor is registered scoped — resolve it inside a fresh scope so the DbContext and
        // any scoped collaborators are disposed per cycle.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<IPaymentEnrollmentIntentProcessor>();
        var recovered = await processor.RecoverPendingIntentsAsync(batchSize, ct);

        if (recovered > 0)
        {
            _logger.LogInformation(
                "PaymentEnrollmentIntentRecoveryProcessor recovered {RecoveredCount} pending intent(s).",
                recovered);
        }

        return recovered;
    }
}
