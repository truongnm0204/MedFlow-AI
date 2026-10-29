export type AppointmentRecordStatus =
  'CONFIRMED' | 'BOOKED' | 'PENDING' | 'CHECKED_IN' | 'COMPLETED' | 'CANCELLED' | 'NO_SHOW';

export type AppointmentTabGroup = 'ALL' | 'UPCOMING' | 'COMPLETED' | 'CANCELLED';

export type BookingChannel = 'ONLINE' | 'COUNTER';

export type AppointmentPaymentStatus = 'UNPAID_AT_COUNTER' | 'PAID_VIETQR' | 'REFUNDED_OR_FREE';

export interface AppointmentMemberOption {
  id: string;
  patientCode: string;
  fullName: string;
  relationshipLabel: string;
  ageLabel: string;
}

export interface AppointmentListItem {
  id: string;
  appointmentCode: string;
  patientId: string;
  patientCode: string;
  patientName: string;
  patientRelationshipLabel: string;
  patientAgeLabel: string;
  serviceId: string;
  serviceCode: string;
  serviceName: string;
  specialtyId: string;
  specialtyName: string;
  departmentName: string;
  doctorId: string;
  doctorCode: string;
  doctorName: string;
  doctorTitle: string;
  doctorRoleLabel: string;
  roomCode: string;
  roomName: string;
  roomZoneLabel: string;
  scheduledDate: string; // YYYY-MM-DD
  scheduledDateBadge: string; // e.g. 'Hôm nay, 13/10/2026' or '15/08/2026'
  scheduledDateFullLabel: string; // e.g. 'Thứ Ba, 13/10/2026 (Ca Sáng)'
  scheduledTimeRange: string; // e.g. '09:00 - 09:30'
  checkInDeadlineTime?: string; // e.g. '08:45'
  checkInCounterLabel?: string; // e.g. 'Quầy Tiếp đón Tầng 1 - Nhà A'
  queueNumber?: string | null; // e.g. '012'
  bookingChannel: BookingChannel;
  status: AppointmentRecordStatus;
  statusBannerLabel: string; // e.g. 'ĐÃ XÁC NHẬN - SẮP ĐẾN GIỜ KHÁM'
  listedFee: number; // VND, e.g. 250000
  feePolicyNote: string; // e.g. 'Khám Tự Nguyện trọn gói • Không áp dụng BHYT'
  paymentStatus: AppointmentPaymentStatus;
  paymentStatusLabel: string; // e.g. 'Chờ thanh toán tại quầy' or 'Đã thanh toán 250.000 VNĐ qua VietQR'
  paymentSubNote?: string;
  clinicalSummary?: string | null;
  cancellationReason?: string | null;
  slotReleaseNote?: string | null;
  rebookSpecialtyLabel?: string | null;
  bookedAt: string;
}

export interface AppointmentTabCounts {
  all: number;
  upcoming: number;
  completed: number;
  cancelled: number;
}

export interface AppointmentMemberCountItem extends AppointmentMemberOption {
  appointmentCount: number;
}

export interface AppointmentListFilterParams {
  memberId?: string; // 'ALL' or patientId
  tab?: AppointmentTabGroup;
  keyword?: string;
  dateFrom?: string;
  dateTo?: string;
  page?: number;
  pageSize?: number;
}

export interface AppointmentListResponse {
  items: AppointmentListItem[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
  tabCounts: AppointmentTabCounts;
  memberCounts: AppointmentMemberCountItem[];
  totalAllMembersCount: number;
}
