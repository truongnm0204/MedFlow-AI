export interface VisitChargesBriefDto {
  payableTotal: number;
  recognizedPaidTotal: number;
  amountDue: number | null;
}

export interface VisitResumeDiagnosticOrderItemDto {
  orderId: number;
  orderCode: string;
  serviceName: string;
  requiredForConclusion: boolean;
  selectionStatus: string;
  orderStatus: string;
  isPaymentSettled: boolean;
  deferredUntilDate: string | null;
  validityExpiresAt: string | null;
  isExpiredForReview: boolean;
  canResumeToday: boolean;
  validityExtendedUntil?: string | null;
  revalidationNote?: string | null;
}

export interface VisitActiveQueueBriefDto {
  queueEntryId: number;
  ticketNo: string;
  queueNumber: number;
  queueDate: string;
  queueType: string;
  status: string;
  presenceStatus: string;
  servicePointId: number | null;
  servicePointName: string | null;
  roomCode: string | null;
  coordinationPurpose?: string | null;
}

export interface VisitResumeContextResponseDto {
  visitId: number;
  visitCode: string;
  checkedInAt: string | null;
  visitStatus: string;
  presenceState: 'PRESENT' | 'LEFT' | 'DEFERRED' | (string & {});
  lastPresentDate: string | null;
  lastResumedAt: string | null;
  isPresentToday: boolean;
  readyForReturnAt: string | null;
  isReadyForReturn: boolean;
  assignedDoctorId: number | null;
  assignedDoctorName: string | null;
  examinationRoomId: number | null;
  isOverdueForReview: boolean;
  overdueReviewReasons: string[];
  chargesSummary: VisitChargesBriefDto;
  diagnosticOrders: VisitResumeDiagnosticOrderItemDto[];
  activeTodayQueues: VisitActiveQueueBriefDto[];
  isAssignedDoctorActive?: boolean;
  isOverdueReturnApproved?: boolean;
}

export type ResumeCrossDayTarget = 'DIAGNOSTIC' | 'RETURN_CONSULTATION';
export type QueueCoordinationPurpose = 'RESULT_CONCLUSION' | 'CLINICAL_REVIEW';

export interface ResumeCrossDayVisitRequestDto {
  target: ResumeCrossDayTarget;
  diagnosticOrderId?: number | null;
  diagnosticOrderIds?: number[] | null;
  targetServicePointId?: number | null;
  reassignDoctorId?: number | null;
  confirmOverdueOverride?: boolean | null;
  idempotencyKey?: string | null;
  notes?: string | null;
  coordinationPurpose?: QueueCoordinationPurpose | null;
}

export interface ResumeCrossDayVisitResponseDto {
  visitId: number;
  visitCode: string;
  checkedInAt: string | null;
  visitStatus: string;
  presenceState: string;
  lastPresentDate: string;
  lastResumedAt: string;
  target: ResumeCrossDayTarget | (string & {});
  isIdempotentReplay: boolean;
  assignedDoctorId: number | null;
  assignedDoctorName: string | null;
  examinationRoomId: number | null;
  resumedQueues: VisitActiveQueueBriefDto[];
  closedPriorDayQueueCount: number;
  coordinationPurpose?: QueueCoordinationPurpose | (string & {}) | null;
}

export interface DeferVisitOrOrdersRequestDto {
  reason: string;
  deferredUntilDate?: string | null;
  diagnosticOrderIds?: number[] | null;
  patientLeftHospital?: boolean;
}

export interface DeferVisitOrOrdersResponseDto {
  visitId: number;
  visitCode: string;
  visitStatus: string;
  presenceState: string;
  lastPresentDate: string | null;
  readyForReturnAt: string | null;
  deferredUntilDate: string | null;
  deferredQueueEntryIds: number[];
  updatedDiagnosticOrderIds: number[];
}
