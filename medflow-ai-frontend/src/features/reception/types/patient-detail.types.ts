// Types cho man xem chi tiet benh nhan (M2 — mock data, chua co backend API).

export type PatientGender = 'MALE' | 'FEMALE' | 'OTHER';

export type TodayAppointmentStatus = 'BOOKED' | 'ARRIVED' | 'IN_PROGRESS' | 'DONE';

export interface PastVisitSummary {
  id: string;
  date: string; // "12/03/2026"
  department: string;
  doctorName: string;
  diagnosis: string;
}

export interface PatientTodayAppointment {
  timeRange: string; // "09:00 - 09:30"
  doctorName: string;
  room: string;
  status: TodayAppointmentStatus;
}

export interface EmergencyContact {
  name: string;
  relationship: string;
  phone: string;
}

export interface PatientDetail {
  id: string;
  patientCode: string;
  fullName: string;
  gender: PatientGender;
  age: number;
  dateOfBirth: string;
  cccd: string;
  phone: string;
  bhytNumber: string | null;
  address: string;
  emergencyContact: EmergencyContact | null;
  todayAppointment: PatientTodayAppointment | null;
  pastVisits: PastVisitSummary[];
}
