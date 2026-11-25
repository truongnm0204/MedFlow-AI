export type ReminderUrgency = 'URGENT' | 'UPCOMING' | 'NORMAL';

export interface AppointmentReminder {
  id: string;
  appointmentId?: string;
  appointmentCode: string;
  patientName: string;
  doctorName: string;
  doctorAvatar?: string | null;
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
  notificationType?: string;
  createdAt: string;
  createdAtIso?: string;
  isRead: boolean;
  appointmentReminder?: AppointmentReminder | null;
}

export interface NotificationFeedResponse {
  unreadCount: number;
  activeReminder: AppointmentReminder | null;
  items: PatientNotification[];
}

export interface GetNotificationsParams {
  limit?: number;
  unreadOnly?: boolean;
}

export interface MarkNotificationReadResponse {
  id: string;
  isRead: boolean;
  readAtIso: string;
  unreadCount: number;
}

export interface MarkAllNotificationsReadResponse {
  markedCount: number;
  unreadCount: number;
}
