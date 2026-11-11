// Types cho màn Diagnostic Dashboard (M1-015 UI & M1-016 BE Integration).
// Đồng bộ với DTO response của endpoint GET /api/v1/diagnostic/dashboard/summary.

export type DiagnosticOrderStatus =
  'ORDERED' | 'CONFIRMED' | 'IN_PROGRESS' | 'COMPLETED' | 'CANCELLED';

export type DiagnosticPriority = 'NORMAL' | 'URGENT' | 'STAT';

export type DiagnosticCategory = 'LABORATORY' | 'IMAGING' | 'FUNCTIONAL';

export interface DiagnosticQueueEntry {
  id: string;
  orderCode: string; // ma phieu chi dinh, vd "CLS-2026-0041"
  ticketNo: string; // so thu tu hang doi CLS, vd "C018"
  patientName: string;
  patientCode: string;
  serviceName: string;
  category: DiagnosticCategory;
  room: string; // phong thuc hien CLS, vd "P.XN-201"
  orderingDoctor: string;
  orderedAt: string; // gio chi dinh, vd "08:10"
  priority: DiagnosticPriority;
  status: DiagnosticOrderStatus;
}

export interface DiagnosticStats {
  pendingOrders: number; // phieu chi dinh moi cho xac nhan (UC39)
  inQueue: number; // benh nhan da tiep nhan dang cho thuc hien (UC40/UC41)
  inExecution: number; // dang lay mau / chup chieu (UC42)
  completedToday: number; // da hoan tat va tra ket qua trong ngay (UC43)
}

export interface PendingResultEntry {
  id: string;
  orderCode: string;
  patientName: string;
  serviceName: string;
  orderingDoctor: string;
  updatedAt: string;
  status: 'DRAFT' | 'PENDING_VERIFY' | 'FINALIZED';
}

export interface DiagnosticQuickAction {
  id: string;
  label: string;
  description: string;
  icon: string; // SVG path d-attribute
  accent: 'sky' | 'emerald' | 'amber' | 'rose';
  ucCode?: string;
}

export interface DiagnosticStaffProfile {
  userId: number;
  email: string;
  fullName: string;
  staffCode?: string | null;
  hospitalId: number;
  hospitalCode: string;
  hospitalName: string;
  departmentId?: number | null;
  departmentName: string;
  roles: string[];
  permissions: string[];
}

export interface DiagnosticDashboardQueryParams {
  queueLimit?: number;
  pendingResultLimit?: number;
  category?: DiagnosticCategory | 'ALL';
  status?: DiagnosticOrderStatus | 'ALL';
  priority?: DiagnosticPriority | 'ALL';
  keyword?: string;
}

export interface DiagnosticDashboardSummary {
  facilityName: string;
  facilityCode: string;
  departmentName: string;
  shiftLabel: string;
  stats: DiagnosticStats;
  queue: DiagnosticQueueEntry[];
  pendingResults: PendingResultEntry[];
  quickActions: DiagnosticQuickAction[];
  staffProfile?: DiagnosticStaffProfile;
  generatedAtUtc?: string;
}

export * from './diagnostic-order-review.types';
export * from './diagnostic-queue.types';
export * from './diagnostic-patient-receive.types';
export * from './diagnostic-execution.types';
