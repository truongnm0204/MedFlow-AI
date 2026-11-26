namespace MedFlow.Application.Options;

public sealed class ScheduleJobsOptions
{
    public const string SectionName = "ScheduleJobs";

    public bool Enabled { get; set; } = true;
    public int OutboxIntervalSeconds { get; set; } = 30;
    public int LifecycleIntervalMinutes { get; set; } = 10;
    public int RollingSlotCheckHours { get; set; } = 6;
    public int BatchSize { get; set; } = 50;
    public int MaxEmailConcurrency { get; set; } = 5;
    public int MaxRetryCount { get; set; } = 3;
    public int ProcessingLeaseSeconds { get; set; } = 120;
    public int SmtpTimeoutSeconds { get; set; } = 20;
    public int DefaultReminderHours { get; set; } = 24;
    public int DefaultRollingSlotWindowDays { get; set; } = 14;
    public int MaxRollingSlotWindowDays { get; set; } = 60;
    public int DefaultSlotDurationMinutes { get; set; } = 30;
    public int PaymentIntentRecoveryIntervalSeconds { get; set; } = 30;
    public int DefaultSlotCapacity { get; set; } = 3;
}
