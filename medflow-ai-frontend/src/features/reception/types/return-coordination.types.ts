/** Stage E2 — centralized Reception return-coordination contracts (ADR-0061 Part 2 / RC-01..22). */

export type ReturnCoordinationPurpose = 'RESULT_CONCLUSION' | 'CLINICAL_REVIEW';

export interface ReturnCoordinationDoctorCandidateDto {
  doctorId: number;
  doctorCode: string;
  doctorName: string;
  specialtyId: number | null;
  specialtyName: string | null;
  departmentId: number | null;
  departmentName: string | null;
  roomId: number | null;
  roomCode: string | null;
  roomName: string | null;
  servicePointId: number | null;
  servicePointName: string | null;
  isOriginalAssignedDoctor: boolean;
  isEligible: boolean;
  isAtCapacity: boolean;
  isRecommended: boolean;
  recommendationReason: string | null;
  currentServingCount: number;
  waitingExaminationCount: number;
  waitingReturnCount: number;
  upcomingBookedAppointmentsTodayCount: number;
  remainingShiftBookedCount: number;
  remainingShiftMinutes: number | null;
  projectedLoadCount: number;
  ineligibleReasons: string[];
}

export interface VisitQueueEnrollmentTicketDto {
  queueEntryId: string;
  visitId: number;
  queueType: string | null;
  queueNumber: number;
  queueDate: string;
  status: string;
  presenceStatus: string | null;
  servicePointId: number | null;
  servicePointName: string | null;
  coordinationPurpose: string | null;
}

export interface ReturnCoordinationContextDto {
  visitId: number;
  visitCode: string;
  visitStatus: string;
  presenceState: string;
  isPresentToday: boolean;
  presenceSessionNo: number;
  isReadyForReturnConclusion: boolean;
  readyForReturnAt: string | null;
  hasClinicalReviewItems: boolean;
  isReturnOverdue: boolean;
  allowDoctorReassignment: boolean;
  currentAssignedDoctorId: number | null;
  currentAssignedDoctorCode: string | null;
  currentAssignedDoctorName: string | null;
  currentExaminationRoomId: number | null;
  currentExaminationRoomCode: string | null;
  activeQueueEntry: VisitQueueEnrollmentTicketDto | null;
  originalDoctor: ReturnCoordinationDoctorCandidateDto | null;
  eligibleDoctors: ReturnCoordinationDoctorCandidateDto[];
  generatedAtUtc: string;
}

export interface CreateReturnEnrollmentRequestDto {
  doctorId?: number | null;
  roomId?: number | null;
  servicePointId?: number | null;
  purpose?: ReturnCoordinationPurpose | null;
  reassignmentReason?: string | null;
  notes?: string | null;
  idempotencyKey?: string | null;
}

export interface ReturnEnrollmentResponseDto {
  visitId: number;
  visitCode: string;
  visitStatus: string;
  presenceState: string;
  presenceSessionNo: number;
  assignedDoctorId: number;
  assignedDoctorCode: string;
  assignedDoctorName: string;
  doctorWasReassigned: boolean;
  previousAssignedDoctorId: number | null;
  examinationRoomId: number | null;
  examinationRoomCode: string | null;
  examinationRoomName: string | null;
  coordinationPurpose: string;
  ticket: VisitQueueEnrollmentTicketDto;
}
