import type { ReactNode } from 'react';

export interface Specialty {
  id: string;
  name: string;
  desc: string;
  doctors: number;
  price: string;
  tag: string;
  icon: ReactNode;
}

export interface Doctor {
  id: string;
  name: string;
  role: string;
  degree: string;
  hospital: string;
  earliestSlot: string;
  fee: string;
  rating: string;
  avatar: string;
}

export interface JourneyStep {
  step: string;
  title: string;
  desc: string;
}

export type AppointmentStatus = 'CONFIRMED' | 'PENDING' | 'CHECKED_IN';

export interface UpcomingAppointmentItem {
  id: string;
  appointmentCode: string;
  specialtyName: string;
  doctorName: string;
  roomName: string;
  scheduledDate: string;
  scheduledTime: string;
  patientName: string;
  status: AppointmentStatus;
}

export interface PatientQuickAction {
  id: string;
  label: string;
  description: string;
  href: string;
  badgeText?: string;
  accent: 'sky' | 'emerald' | 'amber' | 'indigo';
}

export interface PatientHomeMetrics {
  upcomingAppointmentsCount: number;
  familyMembersCount: number;
  completedVisitsCount: number;
  activePrescriptionsCount: number;
}

export interface PatientHomeSummary {
  patientCode: string;
  fullName: string;
  isVerified: boolean;
  metrics: PatientHomeMetrics;
  upcomingAppointments: UpcomingAppointmentItem[];
  lastUpdatedLabel: string;
}

export interface QuickSlotSearchRequest {
  specialtyId: string;
  doctorId: string;
  dateOption: 'today' | 'tomorrow' | 'weekend';
  symptomNote?: string;
}

export interface QuickSlotItem {
  id: string;
  timeRange: string;
  doctorName: string;
  specialtyName: string;
  roomName: string;
  feeLabel: string;
  availableSeats: number;
}
