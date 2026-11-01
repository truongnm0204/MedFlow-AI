import type { BookingDateOption, BookingPatientProfile } from './slot-booking.types';

export type BookingMode = 'doctor' | 'service';

export interface CreateAppointmentRequest {
  mode: BookingMode;
  doctorId: string | null;
  serviceId: string | null;
  patientId: string;
  dateIso: string;
  slotId: string | null;
  shiftId: string | null;
  reasonForVisit: string;
}

export interface BookingReviewSummary {
  mode: BookingMode;
  patient: BookingPatientProfile;
  date: BookingDateOption;
  timeRange: string;
  shiftLabel: string;
  scheduleLabel: string;
  checkInDeadlineTime: string;
  doctorName: string | null;
  doctorRoleTag: string | null;
  doctorAvatarUrl: string | null;
  specialtyName: string;
  serviceId: string;
  serviceCode: string;
  serviceName: string;
  roomLabel: string;
  durationMinutes: number | null;
  durationDescription: string | null;
  price: number;
}
