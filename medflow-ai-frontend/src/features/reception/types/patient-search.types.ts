export type Gender = 'MALE' | 'FEMALE' | 'OTHER';

export type TodayAppointmentStatus = 'BOOKED' | 'CHECKED_IN' | 'CANCELLED' | 'IN_CONSULTATION';

export interface TodayAppointmentSummary {
  id: string;
  time: string;
  doctorName: string;
  department: string;
  roomName: string;
  status: TodayAppointmentStatus;
}

export interface PatientSearchResultItem {
  id: string;
  patientCode: string;
  fullName: string;
  gender: Gender;
  dateOfBirth: string;
  identityCard: string;
  phoneNumber: string;
  healthInsuranceNumber?: string;
  address: string;
  todayAppointment?: TodayAppointmentSummary;
  totalPastVisits: number;
}

export type SearchFilterTab = 'ALL' | 'HAS_APPOINTMENT' | 'CHECKED_IN' | 'NO_APPOINTMENT';

export interface PatientSearchFilters {
  query: string;
  tab: SearchFilterTab;
}
