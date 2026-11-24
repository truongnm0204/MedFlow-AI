namespace MedFlow.Application.DTOs.SystemAdmin;

public sealed record GetIntegrationsRequest(
    long? HospitalId = null);

public sealed record IntegrationRuntimeSecretStatusDto(
    string SecretName,
    bool IsConfigured,
    string? MaskedValue,
    string SourceHint);

public sealed record VietQrIntegrationSettingsDto(
    string? BankBin,
    string? AccountNumber,
    string? AccountName,
    string TransferPrefix,
    int PendingExpiryMinutes,
    string WebhookEndpointPath);

public sealed record SmtpIntegrationSettingsDto(
    string Host,
    int Port,
    string? SenderIdentity,
    string FromDisplayName,
    string? ReplyToEmail,
    string SubjectPrefix,
    int TimeoutSeconds);

public sealed record AiIntegrationSettingsDto(
    string ProviderName,
    string ModelName,
    int TimeoutSeconds,
    int MaxSuggestions,
    string DisclaimerTemplate);

public sealed record IntegrationProviderItemDto(
    string ProviderKey,
    string DisplayName,
    string Category,
    string Description,
    bool Enabled,
    string Status,
    int PolicyVersion,
    DateTime? LastUpdatedAt,
    IntegrationRuntimeSecretStatusDto SecretStatus,
    VietQrIntegrationSettingsDto? VietQr,
    SmtpIntegrationSettingsDto? Smtp,
    AiIntegrationSettingsDto? Ai);

public sealed record IntegrationConfigHospitalOptionDto(
    long Id,
    string Code,
    string Name);

public sealed record IntegrationConfigListResponseDto(
    long HospitalId,
    string HospitalCode,
    string HospitalName,
    bool CanEdit,
    DateTime GeneratedAtUtc,
    IReadOnlyList<IntegrationProviderItemDto> Integrations,
    IReadOnlyList<IntegrationConfigHospitalOptionDto> AvailableHospitals);

public sealed record UpdateIntegrationConfigRequest
{
    public long? HospitalId { get; init; }
    public bool? Enabled { get; init; }
    public string? Reason { get; init; }

    // VIETQR_SEPAY settings
    public string? BankBin { get; init; }
    public string? AccountNumber { get; init; }
    public string? AccountName { get; init; }
    public string? TransferPrefix { get; init; }
    public int? PendingExpiryMinutes { get; init; }

    // SMTP_EMAIL settings
    public string? FromDisplayName { get; init; }
    public string? ReplyToEmail { get; init; }
    public string? SubjectPrefix { get; init; }

    // Shared / AI_PROVIDER settings
    public string? ProviderName { get; init; }
    public string? ModelName { get; init; }
    public int? TimeoutSeconds { get; init; }
    public int? MaxSuggestions { get; init; }
    public string? DisclaimerTemplate { get; init; }

    // Guard fields: must never be supplied via client API payload
    public string? ApiKey { get; init; }
    public string? Secret { get; init; }
    public string? WebhookApiKey { get; init; }
    public string? Password { get; init; }
}
