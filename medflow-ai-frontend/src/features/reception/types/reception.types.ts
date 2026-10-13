// Types cho man Reception Dashboard (M1 — mock data, chua co backend API).
// Khi M2 co endpoint that, cac shape nay se duoc map sang DTO response.

export type QueueStatus = 'WAITING' | 'CALLED' | 'IN_PROGRESS' | 'DONE' | 'SKIPPED';

export type VisitType = 'SCHEDULED' | 'WALK_IN';

export interface QueueEntry {
  id: string;
  ticketNo: string; // so thu tu / so tiep don, vd "A012"
  patientName: string;
  patientCode: string;
  visitType: VisitType;
  room: string; // phong kham duoc xep, vd "P.102"
  status: QueueStatus;
  checkedInAt: string; // ISO time, vd "08:15"
  priority: 'NORMAL' | 'URGENT' | 'ELDERLY' | 'PREGNANT';
}

export interface ReceptionStats {
  waitingCheckIn: number; // lich hen hom nay chua check-in
  inQueue: number; // dang cho trong hang doi
  inConsultation: number; // dang kham
  completedToday: number; // da xong trong ngay
}

export interface TodayAppointment {
  id: string;
  time: string; // "09:30"
  patientName: string;
  doctorName: string;
  department: string;
  status: 'BOOKED' | 'CHECKED_IN' | 'CANCELLED' | 'NO_SHOW';
}

export interface QuickAction {
  id: string;
  label: string;
  description: string;
  icon: string; // SVG path d-attribute
  accent: 'sky' | 'emerald' | 'amber' | 'rose';
}
