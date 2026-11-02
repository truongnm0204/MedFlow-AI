export type QueueTrackingStatus = 'AWAITING_CHECK_IN' | 'WAITING' | 'COMPLETED';

export type VisitStepState = 'done' | 'current' | 'upcoming';

export interface QueueVisitStep {
  key: 'CHECK_IN' | 'CONSULTATION' | 'COMPLETE';
  title: string;
  state: VisitStepState;
  statusLabel: string | null;
  note: string;
}

export interface AppointmentQueueStatus {
  appointmentId: string;
  appointmentCode: string;
  visitCode: string;
  queueNumber: string;
  status: QueueTrackingStatus;
  statusLabel: string;
  roomName: string;
  doctorName: string;
  departmentName: string;
  servingQueueNumber: string | null;
  nextQueueNumber: string | null;
  estimatedWaitLabel: string | null;
  visitSteps: QueueVisitStep[];
  guidanceNote: string;
}
