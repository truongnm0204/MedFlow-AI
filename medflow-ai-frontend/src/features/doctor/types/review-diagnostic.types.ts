import type {
  ConsultationDiagnosticSummaryDto,
  ConsultationPatientSummaryDto,
  ConsultationWorkspaceState,
  DiagnosticOrderItemStatus,
  DiagnosticOrderPriority,
  DiagnosticServiceCategory,
} from './consultation-workspace.types';

export type DiagnosticResultStatus =
  'PENDING' | 'PRELIMINARY' | 'FINALIZED' | 'VERIFIED' | 'CANCELLED';

export interface ConsultationDiagnosticResultVersionDto {
  versionId: number;
  resultId: number;
  versionNo: number;
  resultText: string | null;
  conclusion: string | null;
  reportFileUrl: string | null;
  isFinalized: boolean;
  enteredByStaffId: number | null;
  enteredByStaffCode: string | null;
  enteredByStaffName: string | null;
  enteredAt: string | null;
  verifiedByStaffId: number | null;
  verifiedByStaffCode: string | null;
  verifiedByStaffName: string | null;
  verifiedAt: string | null;
  correctionReason: string | null;
}

export interface ConsultationDiagnosticResultItemDto {
  orderId: number;
  orderCode: string;
  visitId: number;
  visitCode: string;
  orderingDoctorId: number;
  orderingDoctorCode: string;
  orderingDoctorName: string;
  serviceId: number;
  serviceCode: string;
  serviceName: string;
  category: DiagnosticServiceCategory | (string & {});
  departmentId: number | null;
  departmentCode: string | null;
  departmentName: string;
  specialtyId: number | null;
  specialtyCode: string | null;
  specialtyName: string;
  unitPrice: number;
  requiresResultVerification: boolean;
  priority: DiagnosticOrderPriority | (string & {});
  orderStatus: DiagnosticOrderItemStatus | (string & {});
  requiredForConclusion: boolean;
  instruction: string | null;
  orderedAt: string;
  confirmedAt: string | null;
  confirmedByStaffId: number | null;
  confirmedByStaffCode: string | null;
  confirmedByStaffName: string | null;
  queueEntryId: number | null;
  formattedQueueEntryId: string | null;
  ticketNo: string | null;
  queueNumber: number | null;
  queueStatus: string | null;
  servicePointId: number | null;
  servicePointName: string | null;
  roomId: number | null;
  roomCode: string | null;
  roomName: string | null;
  roomLocation: string | null;
  completedAt: string | null;
  hasResult: boolean;
  isResultReady: boolean;
  isResultFinalized: boolean;
  resultId: number | null;
  resultStatus: DiagnosticResultStatus | (string & {});
  resultFinalizedAt: string | null;
  finalizedByStaffId: number | null;
  finalizedByStaffCode: string | null;
  finalizedByStaffName: string | null;
  latestVersionNo: number | null;
  resultText: string | null;
  conclusion: string | null;
  reportFileUrl: string | null;
  enteredByStaffId: number | null;
  enteredByStaffCode: string | null;
  enteredByStaffName: string | null;
  enteredAt: string | null;
  verifiedByStaffId: number | null;
  verifiedByStaffCode: string | null;
  verifiedByStaffName: string | null;
  verifiedAt: string | null;
  correctionReason: string | null;
  versions: ConsultationDiagnosticResultVersionDto[];
}

export interface DiagnosticResultsReviewResponseDto {
  visitId: number;
  visitCode: string;
  visitStatus: string;
  visitStatusLabel: string;
  workspaceState: ConsultationWorkspaceState | (string & {});
  canViewDiagnosticResults: boolean;
  canAcknowledgeDiagnosticResults: boolean;
  disabledReason: string | null;
  generatedAtUtc: string;
  patient: ConsultationPatientSummaryDto;
  diagnosticSummary: ConsultationDiagnosticSummaryDto;
  totalOrdersCount: number;
  readyResultsCount: number;
  pendingResultsCount: number;
  mandatoryOrdersCount: number;
  mandatoryReadyCount: number;
  mandatoryPendingCount: number;
  items: ConsultationDiagnosticResultItemDto[];
}

export interface ReviewDiagnosticResultsRequest {
  orderIds?: number[] | null;
  orderCodes?: string[] | null;
  clinicalInterpretation?: string | null;
  /** BE alias: used when `clinicalInterpretation` is null (`ClinicalInterpretation ?? ReviewNote`). */
  reviewNote?: string | null;
  clinicalConclusion?: string | null;
  amendmentReason?: string | null;
  expectedStateVersion?: number | null;
  expectedRevisionNo?: number | null;
}
