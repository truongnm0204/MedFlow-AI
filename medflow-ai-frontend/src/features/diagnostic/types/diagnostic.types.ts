// Types cho man Diagnostic Dashboard (M1-015 — mock data, chua co backend API).
// Khi M3 co endpoint diagnostic_orders / diagnostic_results that, cac shape nay se map sang DTO response.

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
}
