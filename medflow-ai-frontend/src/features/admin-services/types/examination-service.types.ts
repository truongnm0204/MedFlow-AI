export type ServiceCategory = 'Khám Lâm sàng' | 'Hội chẩn Lâm sàng' | 'Cận lâm sàng kỹ thuật';

export type ServiceStatus = 'ACTIVE' | 'SUSPENDED';

export type BookingChannel = 'ONLINE_ALLOWED' | 'COUNTER_ONLY';

export interface PriceHistoryEntry {
  id?: number;
  date: string;
  validFrom?: string | null;
  validTo?: string | null;
  price: number;
  decisionNumber: string;
  note: string;
  appliedBy: string;
  isActive?: boolean;
  status?: string;
}

export interface UpdateServicePricePayload {
  price: number;
  decisionNumber: string;
  effectiveDate: string;
  validTo?: string;
  appliedBy: string;
  note?: string;
}

export interface ServicePricingMetrics {
  totalPricedServices: number;
  averagePrice: number;
  minPrice: number;
  maxPrice: number;
  latestDecision: string;
  latestEffectiveDate: string;
}

export interface ExaminationService {
  id: string;
  code: string;
  name: string;
  description: string;
  category: ServiceCategory;
  department: string;
  price: number;
  hasEffectivePrice?: boolean;
  onlineBooking: boolean;
  status: ServiceStatus;
  roomDefault?: string;
  effectiveDate?: string;
  validTo?: string;
  decisionNumber?: string;
  appliedBy?: string;
  priceNote?: string;
  priceHistory?: PriceHistoryEntry[];
  hasActiveBookings?: boolean;
  activeBookingsCount?: number;
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
