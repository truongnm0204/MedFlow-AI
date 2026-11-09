export type CheckInPriority = 'NORMAL' | 'URGENT' | 'ELDERLY' | 'PREGNANT';

export interface ScheduledCheckInAppointment {
  id: string;
  appointmentCode: string;
  patientCode: string;
  patientName: string;
  gender: 'MALE' | 'FEMALE' | 'OTHER';
  birthYear: number;
  identityCard: string;
  phoneNumber: string;
  healthInsuranceNumber?: string;
  doctorName: string;
  department: string;
  roomName: string;
  time: string;
  date: string;
  status: 'BOOKED' | 'CHECKED_IN';
}

export interface CheckInFormData {
  reasonForVisit: string;
  bloodPressure?: string;
  heartRate?: number;
  temperature?: number;
  isIdentityVerified: boolean;
  isInsuranceVerified: boolean;
  priority: CheckInPriority;
}

export interface CheckInSuccessResult {
  visitId: string;
  ticketNo: string;
  patientName: string;
  roomName: string;
  doctorName: string;
  checkedInAt: string;
}
