using System.Globalization;
using System.Security.Claims;
using MedFlow.API.Realtime;
using MedFlow.Application.DTOs.Realtime;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Domain.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace MedFlow.API.Hubs;

[Authorize]
public sealed class ClinicalRealtimeHub : Hub
{
    private readonly IClinicalRealtimeAccessService _accessService;
    private readonly ClinicalRealtimeConnectionRegistry _registry;
    private readonly IReadOnlySet<string> _allowedOrigins;
    private readonly ILogger<ClinicalRealtimeHub> _logger;

    public ClinicalRealtimeHub(
        IClinicalRealtimeAccessService accessService,
        ClinicalRealtimeConnectionRegistry registry,
        IConfiguration configuration,
        IHostEnvironment environment,
        ILogger<ClinicalRealtimeHub> logger)
    {
        _accessService = accessService;
        _registry = registry;
        _allowedOrigins = ResolveAllowedOrigins(configuration, environment);
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        EnsureAllowedWebSocketOrigin();

        var userId = ExtractAuthenticatedUserId();
        try
        {
            await _accessService.EnsureConnectedUserActiveAsync(userId, Context.ConnectionAborted);
        }
        catch (Exception ex) when (IsDomainAccessException(ex))
        {
            Context.Abort();
            throw TranslateDomainException(ex);
        }

        _registry.RegisterConnection(Context.ConnectionId, userId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _registry.UnregisterConnection(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public Task<RealtimeSubscriptionAckDto> JoinVisitPaymentGroup(string visitIdOrCode) =>
        ExecuteAuthorizedJoinAsync(
            rawAliasKey: visitIdOrCode,
            authorizeAsync: (userId, ct) => _accessService.AuthorizeVisitPaymentJoinAsync(userId, visitIdOrCode, ct));

    public Task LeaveVisitPaymentGroup(string visitIdOrCode) =>
        ExecuteLeaveAsync("visit-payment:", visitIdOrCode);

    public Task<RealtimeSubscriptionAckDto> JoinAppointmentQueueGroup(string appointmentIdOrCode) =>
        ExecuteAuthorizedJoinAsync(
            rawAliasKey: appointmentIdOrCode,
            authorizeAsync: (userId, ct) => _accessService.AuthorizeAppointmentQueueJoinAsync(userId, appointmentIdOrCode, ct));

    public Task LeaveAppointmentQueueGroup(string appointmentIdOrCode) =>
        ExecuteLeaveAsync("appointment-queue:", appointmentIdOrCode);

    public Task<RealtimeSubscriptionAckDto> JoinServicePointGroup(string servicePointId) =>
        ExecuteAuthorizedJoinAsync(
            rawAliasKey: servicePointId,
            authorizeAsync: (userId, ct) => _accessService.AuthorizeServicePointJoinAsync(userId, servicePointId, ct));

    public Task LeaveServicePointGroup(string servicePointId) =>
        ExecuteLeaveAsync("service-point:", servicePointId);

    public Task<RealtimeSubscriptionAckDto> JoinHospitalQueueGroup() =>
        ExecuteAuthorizedJoinAsync(
            rawAliasKey: null,
            authorizeAsync: (userId, ct) => _accessService.AuthorizeHospitalQueueJoinAsync(userId, ct));

    public Task LeaveHospitalQueueGroup() =>
        ExecuteLeaveAsync("hospital-queue:", null);

    public Task LeaveSubscription(string resourceKey) =>
        ExecuteLeaveAsync(null, resourceKey);

    private async Task<RealtimeSubscriptionAckDto> ExecuteAuthorizedJoinAsync(
        string? rawAliasKey,
        Func<long, CancellationToken, Task<AuthorizedRealtimeSubscription>> authorizeAsync)
    {
        EnsureInvocationRateLimit();
        var userId = ExtractAuthenticatedUserId();
        var ct = Context.ConnectionAborted;

        AuthorizedRealtimeSubscription grant;
        try
        {
            grant = await authorizeAsync(userId, ct);
        }
        catch (Exception ex) when (IsDomainAccessException(ex))
        {
            throw TranslateDomainException(ex);
        }

        var outcome = _registry.TryUpsertSubscription(Context.ConnectionId, userId, grant, rawAliasKey);
        foreach (var expiredGroup in outcome.PrunedExpiredGroups)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, expiredGroup, ct);

        if (!outcome.Allowed)
            throw new HubException(outcome.ErrorMessage ?? "[GROUP_LIMIT_EXCEEDED] Không thể tham gia thêm nhóm thời gian thực.");

        foreach (var staleGroup in outcome.GroupsToRemove)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, staleGroup, ct);

        foreach (var group in outcome.GroupsToAdd)
            await Groups.AddToGroupAsync(Context.ConnectionId, group, ct);

        return new RealtimeSubscriptionAckDto(grant.ResourceKey, outcome.ExpiresAtUtc);
    }

    private async Task ExecuteLeaveAsync(string? resourceKeyPrefix, string? rawIdentifier)
    {
        EnsureInvocationRateLimit();
        var ct = Context.ConnectionAborted;
        var outcome = _registry.LeaveByResourceOrAlias(Context.ConnectionId, resourceKeyPrefix, rawIdentifier);

        foreach (var expiredGroup in outcome.PrunedExpiredGroups)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, expiredGroup, ct);

        foreach (var group in outcome.GroupsToRemove)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, group, ct);
    }

    private void EnsureInvocationRateLimit()
    {
        if (!_registry.TryCheckInvocationRateLimit(Context.ConnectionId, out var errorMessage))
            throw new HubException(errorMessage ?? "[RATE_LIMITED] Quá giới hạn tần suất yêu cầu.");
    }

    private void EnsureAllowedWebSocketOrigin()
    {
        var httpContext = Context.GetHttpContext();
        if (httpContext is null)
            return;

        var rawOrigin = httpContext.Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(rawOrigin))
            return;

        var normalizedOrigin = rawOrigin.Trim().TrimEnd('/');
        var sameOrigin = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";

        if (string.Equals(normalizedOrigin, sameOrigin, StringComparison.OrdinalIgnoreCase) ||
            _allowedOrigins.Contains(normalizedOrigin))
        {
            return;
        }

        _logger.LogWarning(
            "Rejected SignalR connection {ConnectionId} from disallowed Origin.",
            Context.ConnectionId);
        Context.Abort();
        throw new HubException("[FORBIDDEN] Origin không được phép kết nối kênh thời gian thực.");
    }

    private long ExtractAuthenticatedUserId()
    {
        var rawUserId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Context.User?.FindFirstValue("sub");

        if (string.IsNullOrWhiteSpace(rawUserId) ||
            !long.TryParse(rawUserId, NumberStyles.None, CultureInfo.InvariantCulture, out var userId) ||
            userId <= 0)
        {
            throw new HubException("[UNAUTHORIZED] Thông tin định danh người dùng không hợp lệ.");
        }

        return userId;
    }

    private static bool IsDomainAccessException(Exception ex) =>
        ex is UnauthorizedAccessException
            or ForbiddenException
            or NotFoundException
            or ValidationException
            or ConflictException;

    private static HubException TranslateDomainException(Exception ex) => ex switch
    {
        UnauthorizedAccessException ua => new HubException($"[UNAUTHORIZED] {ua.Message}"),
        ForbiddenException fe => new HubException($"[FORBIDDEN] {fe.Message}"),
        NotFoundException nf => new HubException($"[NOT_FOUND] {nf.Message}"),
        ValidationException ve => new HubException($"[VALIDATION_ERROR] {ve.Message}"),
        ConflictException ce => new HubException($"[CONFLICT] {ce.Message}"),
        _ => new HubException("[ERROR] Không thể thực hiện yêu cầu trên kênh thời gian thực."),
    };

    private static HashSet<string> ResolveAllowedOrigins(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var origins = configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>()?
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim().TrimEnd('/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase)
            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (origins.Count == 0 && (environment.IsDevelopment() || environment.IsEnvironment("Test")))
        {
            origins.Add("http://localhost:5173");
            origins.Add("https://localhost:5173");
        }

        return origins;
    }
}
