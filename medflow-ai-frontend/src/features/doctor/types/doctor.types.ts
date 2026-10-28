// Types cho màn Doctor Dashboard (M1 Role Landing — mock data, chưa có backend API).
// Khi M2/M3 bổ sung endpoint Visit/Queue/Consultation/Diagnostic, các shape này sẽ ánh xạ sang DTO.

export type AssignedQueueStatus =
  'WAITING' | 'CALLED' | 'IN_CONSULTATION' | 'AWAITING_RESULTS' | 'COMPLETED';

export type VisitPriority = 'NORMAL' | 'URGENT' | 'ELDERLY' | 'RETURN_RESULT';

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
  shortcut: string;
  icon: string; // SVG path d-attribute
  accent: 'sky' | 'emerald' | 'amber' | 'rose';
}
