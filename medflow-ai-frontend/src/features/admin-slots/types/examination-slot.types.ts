export type SlotStatus = 'OPEN' | 'FULL' | 'CLOSED';
export type ServiceType = 'OUTPATIENT' | 'DIAGNOSTIC' | 'INPATIENT';

export interface ExaminationSlot {
  id: string;
  code: string;
  doctorId: string;
  doctorName: string;
  serviceId: string;
  serviceName: string;
  serviceCode: string;
  serviceType: ServiceType;
  specialty: string;
  room: string;
  capacityBooked: number;
  capacityMax: number;
  date: string; // 'YYYY-MM-DD'
  dateDisplay: string; // 'DD/MM'
  startTime: string; // 'HH:mm'
  endTime: string; // 'HH:mm'
  status: SlotStatus;
  facility?: string;
  needsReview?: boolean;
  closedAt?: string;
  closeReason?: string;
  closeReasonCode?: CloseSlotReasonCode;
  affectedBookingsAction?: AffectedBookingsAction;
}

export type CloseSlotReasonCode =
  'DOCTOR_UNAVAILABLE' | 'EQUIPMENT_MAINTENANCE' | 'SCHEDULE_ADJUSTMENT' | 'EMERGENCY' | 'OTHER';

export type AffectedBookingsAction = 'KEEP_EXISTING' | 'RESCHEDULE_REQUIRED';

export interface CloseSlotPayload {
  slotId: string;
  reasonCode: CloseSlotReasonCode;
  reasonText?: string;
  note?: string;
  notifyDoctor: boolean;
  notifyPatients: boolean;
  affectedBookingsAction: AffectedBookingsAction;
}
export interface SlotFilterState {
  searchQuery: string;
  dateFilter: string;
  specialtyFilter: string;
  statusFilter: string;
}

export interface SlotMetricsData {
  openCount: number;
  bookedCount: number;
  upcomingCount: number;
  reviewCount: number;
}

export interface ExaminationSlotFormValues {
  code: string;
  serviceType: ServiceType;
  serviceId: string;
  serviceName: string;
  serviceCode: string;
  specialty: string;
  date: string; // YYYY-MM-DD
  startTime: string; // HH:mm
  endTime: string; // HH:mm
  facility: string;
  capacityMax: number;
  room: string;
  doctorId: string;
  doctorName: string;
  status: SlotStatus;
}
