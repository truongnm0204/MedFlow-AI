export type ReviewBookingMode = 'doctor' | 'service';

export interface ReviewBookingQueryParams {
  mode?: ReviewBookingMode;
  doctorId?: string | null;
  serviceId?: string | null;
  serviceCode?: string | null;
  patientId?: string | null;
  dateIso?: string | null;
  slotId?: string | null;
  shiftId?: string | null;
  symptomNote?: string | null;
}

export interface ReviewPatientEhrInfo {
  patientId: string;
  patientCode: string;
  fullName: string;
  relationshipBadgeLabel: string;
  isPrimaryAccount: boolean;
  dateOfBirth: string;
  ageLabel: string;
  gender: 'Nam' | 'Nữ';
  phone: string;
  nationalId: string;
  currentAddress: string;
  symptomNote: string;
}

export interface ReviewScheduleLocationInfo {
  mode: ReviewBookingMode;
  doctorId: string;
  doctorName: string;
  doctorRoleBadge: string;
  doctorCredentialsSubtitle: string;
  doctorAvatarUrl: string;
  specialtyName: string;
  serviceId: string;
  serviceCode: string;
  serviceName: string;
  serviceDisplayLabel: string;
  dateIso: string;
  slotOrShiftId: string;
  scheduledSlotDisplay: string;
  checkInDeadlineTime: string;
  allocatedRoomTitle: string;
  shortRoomNumber: string;
  durationBadgeLabel: string;
  durationFootnote: string;
}

export interface ReviewFeeLineItem {
  id: string;
  title: string;
  description: string;
  amount: number;
  isIncludedFree?: boolean;
  freeBadgeLabel?: string;
}

export interface ReviewFeeSummary {
  serviceTypeBadge: string;
  items: ReviewFeeLineItem[];
  totalAmount: number;
  totalDisclaimer: string;
  paymentMethodNote: string;
}

export interface ReviewReceptionPolicy {
  badgeLabel: string;
  roomNumber: string;
  receptionCounterLabel: string;
  earlyArrivalMinutes: number;
  checkInDeadlineTime: string;
  lateGraceMinutes: number;
}

export interface AppointmentReviewPayload {
  patientInfo: ReviewPatientEhrInfo;
  scheduleLocation: ReviewScheduleLocationInfo;
  feeSummary: ReviewFeeSummary;
  receptionPolicy: ReviewReceptionPolicy;
}

export interface ConfirmBookingRequest {
  mode: ReviewBookingMode;
  patientId: string;
  doctorId: string;
  serviceId: string;
  serviceCode: string;
  dateIso: string;
  slotOrShiftId: string;
  symptomNote?: string;
}

export interface ConfirmBookingResponse {
  appointmentId: string;
  appointmentCode: string;
  queueNumber: string;
  status: 'CONFIRMED';
}
