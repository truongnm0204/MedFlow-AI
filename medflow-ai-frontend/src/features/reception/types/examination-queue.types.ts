export type ExaminationQueueStatus =
  'WAITING' | 'READY' | 'CALLED' | 'IN_PROGRESS' | 'COMPLETED' | 'SKIPPED';

export type ExaminationQueuePriority =
  'NORMAL' | 'URGENT' | 'ELDERLY' | 'PREGNANT' | 'RETURN_RESULT';

export type QueuePresenceStatus = 'PRESENT' | 'NOT_ARRIVED' | 'LEFT';

export type QueueArrivalClass = 'ON_TIME_OR_GRACE' | 'EARLY' | 'LATE' | 'WALK_IN';

export type ExaminationVisitType = 'SCHEDULED' | 'WALK_IN';

export type ExaminationQueueStatusFilter = 'ALL' | ExaminationQueueStatus;
export type ExaminationQueuePriorityFilter = 'ALL' | ExaminationQueuePriority;
export type ExaminationVisitTypeFilter = 'ALL' | ExaminationVisitType;

export interface ExaminationQueueEntryDto {
  id: string;
  queueNumber: number;
  ticketNo: string;
  servingOrder: number;
  visitCode: string;
  patientName: string;
  patientCode: string;
  visitType: ExaminationVisitType;
  servicePointId: string;
  roomCode: string;
  roomName: string;
  departmentName: string;
  doctorName: string;
  status: ExaminationQueueStatus;
  priority: ExaminationQueuePriority;
  presenceStatus: QueuePresenceStatus;
  arrivalClass: QueueArrivalClass;
  scheduledAt: string | null;
  checkedInAt: string;
  calledAt: string | null;
  startedAt: string | null;
  blockedReason: string | null;
}

export interface ServicePointQueueDto {
  servicePointId: string;
  servicePointName: string;
  roomCode: string;
  roomName: string;
  departmentName: string;
  doctorName: string;
  isActive: boolean;
  currentServingTicketNo: string | null;
  nextTicketNo: string | null;
  waitingCount: number;
  inProgressCount: number;
  completedCount: number;
  skippedCount: number;
  entries: ExaminationQueueEntryDto[];
}

export interface ExaminationQueueStatsDto {
  totalEntries: number;
  waitingCount: number;
  calledOrServingCount: number;
  completedCount: number;
  skippedCount: number;
  activeServicePoints: number;
}

export interface ExaminationQueueResponseDto {
  queueDate: string;
  facilityName: string;
  shiftLabel: string;
  generatedAtUtc: string;
  stats: ExaminationQueueStatsDto;
  servicePoints: ServicePointQueueDto[];
}

export interface ExaminationQueueQueryParams {
  queueDate?: string;
  servicePointId?: string;
  status?: ExaminationQueueStatusFilter;
  priority?: ExaminationQueuePriorityFilter;
  visitType?: ExaminationVisitTypeFilter;
  keyword?: string;
}
