export type DiagnosticTrackingOrderStatus = 'SIGNED' | 'AWAITING_SIGN' | 'IN_PROGRESS' | 'PENDING';

export type DiagnosticTrackingVisitStatus = 'COMPLETED' | 'IN_PROGRESS' | 'PENDING_SIGN';

export type DiagnosticTrackingStatusFilter = 'ALL' | DiagnosticTrackingVisitStatus;

export interface DoctorDiagnosticTrackingQueryParams {
  date?: string; // yyyy-MM-dd
  status?: DiagnosticTrackingVisitStatus; // omit for ALL
  keyword?: string;
  page?: number;
  pageSize?: number;
}

export interface DiagnosticTrackingStatsDto {
  totalVisits: number;
  completedVisits: number;
  inProgressVisits: number;
  pendingSignVisits: number;
  totalOrders: number;
  signedOrders: number;
  waitingOrders: number;
}

export interface DiagnosticTrackingOrderDto {
  orderId: number;
  orderCode: string;
  serviceId: number | null;
  serviceCode: string | null;
  serviceName: string | null;
  priority: string | null;
  requiredForConclusion: boolean;
  orderStatus: string;
  trackingStatus: DiagnosticTrackingOrderStatus;
  resultStatus: string;
  orderingDoctorId: number | null;
  orderingDoctorName: string | null;
  orderedAt: string | null;
  assigneeStaffName: string | null;
  resultSummary: string | null;
  signedAt: string | null;
  signedByStaffName: string | null;
}

export interface DiagnosticTrackingVisitDto {
  visitId: number;
  visitCode: string;
  visitStatus: string;
  patientId: number | null;
  patientCode: string | null;
  patientName: string | null;
  trackingStatus: DiagnosticTrackingVisitStatus;
  totalOrders: number;
  signedOrders: number;
  awaitingSignOrders: number;
  inProgressOrders: number;
  pendingOrders: number;
  mandatoryOrders: number;
  mandatorySignedOrders: number;
  completionPercent: number;
  latestOrderedAt: string | null;
  orders: DiagnosticTrackingOrderDto[];
}

export interface DoctorDiagnosticTrackingResponseDto {
  date: string;
  hospitalId: number;
  doctorId: number;
  doctorCode: string | null;
  doctorName: string;
  generatedAtUtc: string;
  stats: DiagnosticTrackingStatsDto;
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  items: DiagnosticTrackingVisitDto[];
}
