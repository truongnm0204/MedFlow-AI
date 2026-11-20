export type CheckInPriority = 'NORMAL' | 'URGENT' | 'ELDERLY' | 'PREGNANT';

export interface ScheduledCheckInAppointment {
  id: string;
  appointmentCode: string;
  /** ID số của lịch hẹn khi backend chỉ trả ID (dùng cho POST check-in). */
  appointmentId?: number;
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
  visitCode?: string;
  ticketNo: string;
  patientName: string;
  patientCode?: string;
  roomName: string;
  departmentName?: string;
  doctorName: string;
  checkedInAt: string;
  priority?: CheckInPriority;
  queueNumber?: number;
  waitingCountBefore?: number;
  estimatedWaitMinutes?: number;
  nextStepInstructions?: string[];
}
