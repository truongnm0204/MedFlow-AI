export type TrackingOrderStatus = 'SIGNED' | 'IN_PROGRESS' | 'PENDING';

export type TrackingVisitStatus = 'COMPLETED' | 'IN_PROGRESS' | 'PENDING_SIGN';

export type TrackingStatusFilter =
  'ALL' | 'COMPLETED' | 'IN_PROGRESS' | 'PENDING_SIGN' | 'ABNORMAL';

export interface TrackingOrder {
  id: string;
  orderCode: string;
  serviceName: string;
  status: TrackingOrderStatus;
  assignee: string;
  resultSummary: string | null;
  isAbnormal: boolean;
  signedAt: string | null;
}

export interface TrackingVisit {
  id: string;
  visitCode: string;
  patientCode: string;
  patientName: string;
  orderingDoctor: string;
  status: TrackingVisitStatus;
  orders: TrackingOrder[];
}

export interface TrackingStats {
  totalOrders: number;
  completed: number;
  waiting: number;
  abnormal: number;
}
