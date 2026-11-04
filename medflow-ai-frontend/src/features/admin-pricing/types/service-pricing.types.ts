export type ServicePricingCategory =
  'Khám Lâm sàng' | 'Hội chẩn Lâm sàng' | 'Cận lâm sàng kỹ thuật';

export type ServicePricingStatus = 'ACTIVE' | 'SUSPENDED' | 'DRAFT';

export interface PriceHistoryRecord {
  date: string;
  price: number;
  decisionNumber: string;
  note: string;
  appliedBy: string;
  effectiveFrom?: string;
  effectiveTo?: string | null;
}

export interface ServicePricingItem {
  id: string;
  code: string;
  name: string;
  description: string;
  category: ServicePricingCategory;
  department: string;
  currentPrice: number;
  formattedPrice: string;
  effectiveDate: string;
  decisionNumber: string;
  status: ServicePricingStatus;
  onlineBooking: boolean;
  roomDefault?: string;
  priceHistory: PriceHistoryRecord[];
}

export type PriceRangeFilter = 'all' | 'under-200k' | '200k-500k' | 'over-500k';

export interface ServicePricingFilter {
  search: string;
  department: string;
  category: string;
  priceRange: PriceRangeFilter;
  status: string;
}

export interface ServicePricingMetrics {
  totalServices: number;

  activePricingCount: number;
  suspendedCount: number;
  averagePrice: number;
  highestPrice: number;
  lowestPrice: number;
}

export interface ServicePricingResponse {
  data: ServicePricingItem[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
  metrics: ServicePricingMetrics;
}
export interface UpdateServicePricePayload {
  newPrice: number;
  effectiveDate: string;
  decisionNumber: string;
  appliedBy: string;
  status: ServicePricingStatus;
  note: string;
}
