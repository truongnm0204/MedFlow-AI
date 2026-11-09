import type {
  ExaminationQueueEntryDto,
  ExaminationQueuePriority,
  ExaminationQueueStatus,
  ExaminationQueueType,
  QueueArrivalClass,
  QueuePresenceStatus,
  ServicePointQueueDto,
} from './examination-queue.types';

export const ALLOWED_QUEUE_STATUS_TRANSITIONS: Readonly<
  Record<ExaminationQueueStatus, readonly ExaminationQueueStatus[]>
> = {
  WAITING: ['READY', 'CALLED', 'SKIPPED'],
  READY: ['CALLED', 'WAITING', 'SKIPPED'],
  CALLED: ['IN_PROGRESS', 'WAITING', 'SKIPPED'],
  IN_PROGRESS: ['COMPLETED', 'WAITING', 'SKIPPED'],
  SKIPPED: ['WAITING', 'READY', 'CALLED'],
  COMPLETED: [],
};

export interface UpdateQueueStatusTarget {
  entryId: string;
  queueNumber: number;
  ticketNo: string;
  visitCode: string;
  patientName: string;
  patientCode: string;
  roomCode: string;
  roomName: string;
  departmentName: string;
  doctorName: string;
  currentStatus: ExaminationQueueStatus;
  presenceStatus: QueuePresenceStatus;
  priority: ExaminationQueuePriority;
  blockedReason: string | null;
  stateVersion?: number;
  initialTargetStatus?: ExaminationQueueStatus;
}

export interface UpdateQueueStatusRequestPayload {
  targetStatus: ExaminationQueueStatus;
  presenceStatus?: QueuePresenceStatus | null;
  reason?: string | null;
  blockedReason?: string | null;
  notes?: string | null;
  expectedStateVersion?: number | null;
}

export interface QueueStatusTransitionHistoryItemDto {
  auditLogId: number;
  queueEntryId: number;
  visitId: number | null;
  visitCode: string | null;
  previousStatus: string;
  targetStatus: string;
  previousPresenceStatus: string | null;
  updatedPresenceStatus: string | null;
  previousPriority: string | null;
  updatedPriority: string | null;
  previousStateVersion: number | null;
  updatedStateVersion: number;
  reason: string | null;
  blockedReason: string | null;
  notes: string | null;
  updatedByUserId: number | null;
  updatedByStaffCode: string | null;
  updatedByName: string | null;
  updatedAtUtc: string;
}

export interface QueueEntryStatusDetailDto {
  queueEntryId: number;
  visitId: number | null;
  visitCode: string | null;
  appointmentId: number | null;
  patientId: number | null;
  patientCode: string | null;
  patientName: string | null;
  hospitalId: number;
  departmentId: number | null;
  departmentCode: string | null;
  departmentName: string | null;
  roomId: number | null;
  roomCode: string | null;
  roomName: string | null;
  assignedDoctorId: number | null;
  assignedDoctorCode: string | null;
  assignedDoctorName: string | null;
  servicePointId: number | null;
  servicePointName: string | null;
  queueDate: string | null;
  queueNumber: number;
  ticketNo: string;
  queueType: string;
  currentStatus: ExaminationQueueStatus;
  visitStatus: string | null;
  presenceStatus: QueuePresenceStatus;
  priority: ExaminationQueuePriority;
  arrivalClass: QueueArrivalClass;
  stateVersion: number;
  scheduledAt: string | null;
  arrivedAt: string | null;
  readyAt: string | null;
  calledAt: string | null;
  startedAt: string | null;
  completedAt: string | null;
  blockedReason: string | null;
  allowedTargetStatuses: ExaminationQueueStatus[];
  statusHistory: QueueStatusTransitionHistoryItemDto[];
}

export interface UpdateQueueStatusResponseDto {
  queueEntryId: number;
  visitId: number | null;
  visitCode: string | null;
  appointmentId: number | null;
  patientId: number | null;
  patientCode: string | null;
  patientName: string | null;
  hospitalId: number;
  departmentId: number | null;
  departmentCode: string | null;
  departmentName: string | null;
  roomId: number | null;
  roomCode: string | null;
  roomName: string | null;
  assignedDoctorId: number | null;
  assignedDoctorCode: string | null;
  assignedDoctorName: string | null;
  servicePointId: number | null;
  servicePointName: string | null;
  queueDate: string | null;
  queueNumber: number;
  ticketNo: string;
  queueType: string;
  previousStatus: ExaminationQueueStatus;
  currentStatus: ExaminationQueueStatus;
  previousVisitStatus: string | null;
  visitStatus: string | null;
  previousPresenceStatus: QueuePresenceStatus;
  presenceStatus: QueuePresenceStatus;
  priority: ExaminationQueuePriority;
  arrivalClass: QueueArrivalClass;
  previousStateVersion: number;
  stateVersion: number;
  scheduledAt: string | null;
  arrivedAt: string | null;
  readyAt: string | null;
  calledAt: string | null;
  startedAt: string | null;
  completedAt: string | null;
  blockedReason: string | null;
  reason: string | null;
  notes: string | null;
  updatedByUserId: number;
  updatedByStaffCode: string | null;
  updatedByName: string;
  updatedAtUtc: string;
  allowedTargetStatuses: ExaminationQueueStatus[];
  statusHistory: QueueStatusTransitionHistoryItemDto[];
}

export interface UpdateQueuePriorityRequestPayload {
  priority: ExaminationQueuePriority;
  reason: string;
  verificationConfirmed: boolean;
  expectedStateVersion: number;
}

export interface UpdateQueuePriorityResponseDto {
  queueEntryId: number;
  visitId: number | null;
  visitCode: string | null;
  ticketNo: string;
  queueNumber: number;
  queueType: ExaminationQueueType;
  currentStatus: ExaminationQueueStatus;
  previousPriority: ExaminationQueuePriority;
  priority: ExaminationQueuePriority;
  verificationRequired: boolean;
  verificationConfirmed: boolean;
  reason: string;
  previousStateVersion: number;
  stateVersion: number;
  updatedByUserId: number;
  updatedByStaffCode: string | null;
  updatedByName: string;
  updatedAtUtc: string;
}

/** Trạng thái nguồn được phép hủy lượt (ADR 0016). `RESERVED` được BE map sang `WAITING` khi đọc danh sách. */
export const CANCELLABLE_QUEUE_STATUSES: readonly ExaminationQueueStatus[] = [
  'WAITING',
  'READY',
  'CALLED',
  'SKIPPED',
];

export interface CancelQueueEntryTarget {
  entry: ExaminationQueueEntryDto;
  servicePoint: ServicePointQueueDto;
}

export interface CancelQueueEntryRequestPayload {
  reason: string;
  expectedStateVersion: number;
  notes: string | null;
}

export type CancelQueueEntryResponseDto = Omit<
  UpdateQueueStatusResponseDto,
  'currentStatus' | 'allowedTargetStatuses'
> & {
  currentStatus: 'CANCELLED';
  allowedTargetStatuses: [];
};
