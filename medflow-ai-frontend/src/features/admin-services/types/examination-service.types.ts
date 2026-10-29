export type ServiceCategory = 'Khám Lâm sàng' | 'Hội chẩn Lâm sàng' | 'Cận lâm sàng kỹ thuật';

export type ServiceStatus = 'ACTIVE' | 'SUSPENDED';

export type BookingChannel = 'ONLINE_ALLOWED' | 'COUNTER_ONLY';

export interface PriceHistoryEntry {
  date: string;
  price: number;
  decisionNumber: string;
  note: string;
  appliedBy: string;
}

export interface ExaminationService {
  id: string;
  code: string;
  name: string;
  description: string;
  category: ServiceCategory;
  department: string;
  price: number;
  onlineBooking: boolean;
  status: ServiceStatus;
  roomDefault?: string;
  effectiveDate?: string;
  decisionNumber?: string;
  priceHistory?: PriceHistoryEntry[];
}

export type ColumnKey =
  'code' | 'name' | 'category' | 'department' | 'price' | 'booking' | 'status' | 'actions';

export interface ColumnDefinition {
  key: ColumnKey;
  label: string;
  visible: boolean;
  required?: boolean;
}

export interface ServiceFilterState {
  searchQuery: string;
  category: string;
  department: string;
  bookingChannel: string;
  status: string;
}
