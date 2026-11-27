using MedFlow.Application.Interfaces.Persistence;
using MedFlow.Application.Interfaces.Services;
using MedFlow.Infrastructure.Identity;
using MedFlow.Infrastructure.Integrations.Notification;
using MedFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MedFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

        services.AddDbContext<MedFlowDbContext>(options =>
            options.UseNpgsql(connectionString));

        services.AddScoped<IApplicationDbContext>(sp =>
            sp.GetRequiredService<MedFlowDbContext>());
        services.AddScoped<IAppointmentBookingLock, AppointmentBookingLock>();
        services.AddScoped<IPaymentGatewayEventStore, PaymentGatewayEventStore>();
        services.AddScoped<IPatientIdentityClaimStore, PatientIdentityClaimStore>();

        services.AddScoped<IJwtTokenService, JwtTokenService>();
        services.AddScoped<IAuthSessionService, AuthSessionService>();
        services.AddTransient<IEmailSender, SmtpEmailSender>();

        services.AddOptions<MedFlow.Application.Options.ScheduleJobsOptions>()
            .Bind(configuration.GetSection(MedFlow.Application.Options.ScheduleJobsOptions.SectionName))
            .Validate(
                o => o.OutboxIntervalSeconds > 0 &&
                     o.LifecycleIntervalMinutes > 0 &&
                     o.RollingSlotCheckHours > 0 &&
                     o.BatchSize > 0 &&
                     o.MaxEmailConcurrency > 0 &&
                     o.MaxRetryCount > 0 &&
                     o.SmtpTimeoutSeconds > 0 &&
                     o.ProcessingLeaseSeconds > o.SmtpTimeoutSeconds &&
                     o.DefaultReminderHours > 0 &&
                     o.DefaultRollingSlotWindowDays > 0 &&
                     o.MaxRollingSlotWindowDays >= o.DefaultRollingSlotWindowDays &&
                     o.DefaultSlotDurationMinutes >= 10 &&
                     o.DefaultSlotCapacity > 0,
                "ScheduleJobs options must specify positive intervals/capacities and ProcessingLeaseSeconds > SmtpTimeoutSeconds.")
            .ValidateOnStart();

        services.AddSingleton<BackgroundJobs.NotificationOutboxProcessor>();
        services.AddHostedService(sp => sp.GetRequiredService<BackgroundJobs.NotificationOutboxProcessor>());

        services.AddSingleton<BackgroundJobs.AppointmentLifecycleProcessor>();
        services.AddHostedService(sp => sp.GetRequiredService<BackgroundJobs.AppointmentLifecycleProcessor>());

        services.AddSingleton<BackgroundJobs.RollingExaminationSlotGenerator>();
        services.AddHostedService(sp => sp.GetRequiredService<BackgroundJobs.RollingExaminationSlotGenerator>());

        services.AddSingleton<BackgroundJobs.PaymentEnrollmentIntentRecoveryProcessor>();
        services.AddHostedService(sp => sp.GetRequiredService<BackgroundJobs.PaymentEnrollmentIntentRecoveryProcessor>());

        return services;
    }
}
