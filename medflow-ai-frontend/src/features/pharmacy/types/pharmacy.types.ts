// Types cho màn Pharmacy Dashboard (M1-017 Role Landing — mock data, chưa có backend API).
// Khi M3 bổ sung endpoint prescriptions / stock_batches / dispensing, các shape này sẽ ánh xạ sang DTO.

export type PrescriptionQueueStatus = 'PENDING' | 'PREPARING' | 'READY' | 'DISPENSED';

export type PaymentVerificationStatus = 'PAID_QR' | 'PENDING_PAYMENT';

export type FefoBatchAlertStatus = 'FEFO_PRIORITY' | 'LOW_STOCK' | 'EXPIRED_LOCKED';

export interface PrescriptionLineItem {
  medicineCode: string;
  medicineName: string;
  dosageInstruction: string;
  quantityLabel: string;
  batchCode: string;
  expiryDate: string;
}

export interface PrescriptionQueueEntry {
  id: string;
  prescriptionCode: string; // mã đơn thuốc, vd "RX-2610-014"
  ticketNo: string; // số thứ tự quầy dược, vd "P014"
  patientName: string;
  patientCode: string;
  patientAge: number;
  prescribingDoctor: string;
  departmentRoom: string;
  prescribedAt: string;
  itemCount: number;
  fefoSummary: string;
  paymentStatus: PaymentVerificationStatus;
  status: PrescriptionQueueStatus;
  stockTransactionCode?: string;
  allergyNote: string;
  items: PrescriptionLineItem[];
}

export interface PharmacyStats {
  pendingPrescriptions: number; // đơn mới chờ tiếp nhận & soạn (UC44)
  preparingCount: number; // đơn đang soạn & chọn lô FEFO (UC45)
  readyToDispense: number; // đơn đã soạn xong chờ gọi BN nhận thuốc (UC46)
  dispensedToday: number; // đơn đã hoàn tất xuất kho trong ca (UC46)
}

export interface FefoBatchAlertItem {
  id: string;
  medicineName: string;
  batchCode: string;
  expiryDate: string;
  availableStock: string;
  status: FefoBatchAlertStatus;
}

export type PharmacyQuickActionIcon = 'search' | 'prepare' | 'fefo' | 'dispense';

export interface PharmacyQuickAction {
  id: string;
  label: string;
  description: string;
  shortcut: string;
  icon: PharmacyQuickActionIcon;
  accent: 'sky' | 'emerald' | 'amber' | 'rose';
}
