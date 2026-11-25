export type IntegrationProviderKey = 'VIETQR_SEPAY' | 'SMTP_EMAIL' | 'AI_PROVIDER';

export type IntegrationStatus = 'ACTIVE' | 'DISABLED' | 'NEEDS_SETUP' | (string & {});

export type AiProviderEngine = 'GEMINI' | 'OPENAI' | 'DETERMINISTIC_CLINICAL';

export interface IntegrationRuntimeSecretStatusDto {
  secretName: string;
  isConfigured: boolean;
  maskedValue: string | null;
  sourceHint: string;
}

export interface VietQrIntegrationSettingsDto {
  bankBin: string | null;
  accountNumber: string | null;
  accountName: string | null;
  transferPrefix: string;
  pendingExpiryMinutes: number;
  webhookEndpointPath: string;
}

export interface SmtpIntegrationSettingsDto {
  host: string;
  port: number;
  senderIdentity: string | null;
  fromDisplayName: string;
  replyToEmail: string | null;
  subjectPrefix: string;
  timeoutSeconds: number;
}

export interface AiIntegrationSettingsDto {
  providerName: AiProviderEngine | (string & {});
  modelName: string;
  timeoutSeconds: number;
  maxSuggestions: number;
  disclaimerTemplate: string;
}

export interface IntegrationProviderItemDto {
  providerKey: IntegrationProviderKey;
  displayName: string;
  category: string;
  description: string;
  enabled: boolean;
  status: IntegrationStatus;
  policyVersion: number;
  lastUpdatedAt: string | null;
  secretStatus: IntegrationRuntimeSecretStatusDto;
  vietQr: VietQrIntegrationSettingsDto | null;
  smtp: SmtpIntegrationSettingsDto | null;
  ai: AiIntegrationSettingsDto | null;
}

export interface IntegrationConfigHospitalOptionDto {
  id: number;
  code: string;
  name: string;
}

export interface IntegrationConfigListResponseDto {
  hospitalId: number;
  hospitalCode: string;
  hospitalName: string;
  canEdit: boolean;
  generatedAtUtc: string;
  integrations: IntegrationProviderItemDto[];
  availableHospitals: IntegrationConfigHospitalOptionDto[];
}

export interface UpdateIntegrationConfigPayload {
  hospitalId?: number | null;
  enabled: boolean;
  reason?: string;

  // VIETQR_SEPAY
  bankBin?: string;
  accountNumber?: string;
  accountName?: string;
  transferPrefix?: string;
  pendingExpiryMinutes?: number;

  // SMTP_EMAIL
  fromDisplayName?: string;
  replyToEmail?: string;
  subjectPrefix?: string;

  // Shared / AI_PROVIDER
  providerName?: string;
  modelName?: string;
  timeoutSeconds?: number;
  maxSuggestions?: number;
  disclaimerTemplate?: string;
}
