// Types cho màn Doctor Dashboard (M1 Role Landing — mock data, chưa có backend API).
// Khi M2/M3 bổ sung endpoint Visit/Queue/Consultation/Diagnostic, các shape này sẽ ánh xạ sang DTO.

export type AssignedQueueStatus =
  'WAITING' | 'CALLED' | 'IN_CONSULTATION' | 'AWAITING_RESULTS' | 'COMPLETED';

export type VisitPriority = 'NORMAL' | 'ELDERLY' | 'PREGNANT';

export type AssignedQueueType = 'EXAMINATION' | 'RETURN_CONSULTATION';

export interface AssignedPatientEntry {
  id: string;
  queueNo: string; // số thứ tự khám tại phòng, vd "B001"
  patientName: string;
  patientCode: string;
  age: number;
  gender: 'Nam' | 'Nữ';
  chiefComplaint: string; // lý do khám / triệu chứng chính
  assignedAt: string; // giờ phân công vào phòng, vd "08:05"
  priority: VisitPriority;
  queueType: AssignedQueueType;
  status: AssignedQueueStatus;
}

export interface DoctorStats {
  assignedWaiting: number; // bệnh nhân đang chờ gọi khám tại phòng
  inConsultation: number; // ca đang khám lâm sàng
  pendingLabResults: number; // ca chờ / vừa có kết quả cận lâm sàng
  completedToday: number; // ca đã hoàn tất kết luận trong ngày
}

export interface PendingDiagnosticItem {
  id: string;
  orderCode: string;
  patientName: string;
  patientCode: string;
  serviceName: string;
  orderedAt: string;
  status: 'RESULT_READY' | 'IN_PROGRESS';
}

export interface DoctorQuickAction {
  id: string;
  label: string;
  description: string;
  ucCode?: string;
  shortcut: string;
  route?: string;
  icon: string; // SVG path d-attribute
  accent: 'sky' | 'emerald' | 'amber' | 'rose';
}

export interface DoctorDashboardProfile {
  userId: number;
  doctorId: number;
  doctorCode: string;
  staffCode?: string | null;
  email: string;
  fullName: string;
  professionalTitle?: string | null;
  displayName: string;
  specialtyName?: string | null;
  departmentId?: number | null;
  departmentName: string;
  hospitalId: number;
  hospitalCode: string;
  hospitalName: string;
  roomCode: string;
  roomName?: string;
  licenseNumber?: string | null;
  roles: string[];
  permissions: string[];
}

export interface DoctorDashboardQueryParams {
  queueLimit?: number;
  diagnosticLimit?: number;
  status?: AssignedQueueStatus | 'ALL';
  priority?: VisitPriority | 'ALL';
  keyword?: string;
}

export interface DoctorDashboardSummary {
  facilityName: string;
  facilityCode: string;
  roomCode: string;
  roomName?: string;
  departmentName: string;
  shiftLabel: string;
  stats: DoctorStats;
  assignedQueue: AssignedPatientEntry[];
  pendingDiagnostics: PendingDiagnosticItem[];
  quickActions: DoctorQuickAction[];
  doctorProfile?: DoctorDashboardProfile;
  generatedAtUtc?: string;
}
