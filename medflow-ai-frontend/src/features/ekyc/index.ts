/**
 * Điểm vào công khai của feature eKYC.
 *
 * Phán quyết xác minh chỉ đến từ `finalizeEkycSession`; không nơi nào trong feature này
 * diễn giải kết quả SDK.
 */

// Components
export { EkycConsentScreen } from './components/ekyc-consent-screen';
export type { EkycConsentScreenProps } from './components/ekyc-consent-screen';
export { EkycCaptureWizard } from './components/ekyc-capture-wizard';
export type { EkycCaptureWizardProps } from './components/ekyc-capture-wizard';

// Pages
export {
  EkycVerificationPage,
  EKYC_CONSENT_VERSION,
  EKYC_CONSENT_TEXT_PLACEHOLDER,
} from './pages/ekyc-verification-page';
export type { EkycVerificationPageProps } from './pages/ekyc-verification-page';

// Hook
export { useEkycFlow } from './hooks/use-ekyc-flow';
export type { EkycFlowStage, UseEkycFlowResult } from './hooks/use-ekyc-flow';

// API
export {
  createEkycSession,
  finalizeEkycSession,
  getEkycSession,
  createReceptionEkycSession,
  createUnverifiedPatientProfile,
  getReceptionEkycSession,
} from './api/ekyc.api';

// Error helpers
export { getEkycFailureMessage, isEkycTerminal, isRetriableFailure } from './utils/ekyc-errors';

// SDK
export { buildEkycSdkConfig, EKYC_SDK_CONTAINER_ID } from './sdk/ekyc-sdk-config';
export { loadVnptEkycSdk } from './sdk/vnpt-sdk-loader';

// Types
export type {
  CreateEkycSessionRequest,
  CreateEkycSessionResponse,
  CreateReceptionEkycSessionRequest,
  CreateUnverifiedPatientProfileRequest,
  CreateUnverifiedPatientProfileResponse,
  EkycSessionStatusResponse,
  FinalizeEkycSessionResponse,
  EkycVerificationStatus,
  EkycFailureCode,
  EkycVerificationMode,
  EkycPurpose,
  EkycActivationOutcome,
  PatientIdentityStatus,
  PatientUnverifiedReason,
  EkycBindingState,
} from './types/ekyc.types';
export {
  EKYC_TERMINAL_STATUSES,
  PATIENT_UNVERIFIED_REASONS,
  PATIENT_UNVERIFIED_REASONS_REQUIRING_IDENTIFICATION,
} from './types/ekyc.types';
export type { VnptEkycResult } from './sdk/vnpt-sdk.types';
