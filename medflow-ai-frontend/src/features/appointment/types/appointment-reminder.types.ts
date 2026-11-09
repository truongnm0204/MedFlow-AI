export type ReminderUrgency = 'URGENT' | 'UPCOMING' | 'NORMAL';

export interface AppointmentReminder {
  id: string;
  appointmentCode: string;
  patientName: string;
  doctorName: string;
  doctorAvatar?: string;
  specialtyName: string;
  roomName: string;
  scheduledDate: string;
  scheduledTime: string;
  hoursRemaining: number;
  urgency: ReminderUrgency;
  preparationNotes: string[];
  documentsRequired: string[];
  isConfirmedByPatient: boolean;
  notifiedAt: string;
}

export type NotificationCategory = 'APPOINTMENT_REMINDER' | 'CLINICAL_RESULT' | 'SYSTEM';

export interface PatientNotification {
  id: string;
  title: string;
  message: string;
  category: NotificationCategory;
  createdAt: string;
  isRead: boolean;
  appointmentReminder?: AppointmentReminder;
}
