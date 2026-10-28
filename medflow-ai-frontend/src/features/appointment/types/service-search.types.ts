export type ServiceCategory = 'all' | 'specialty' | 'package' | 'pediatric' | 'general';

export type ServiceBadgeType = 'online' | 'endoscopy' | 'pediatric';

export interface ExaminationService {
  id: string;
  serviceCode: string;
  name: string;
  departmentId: string;
  departmentName: string;
  badgeType?: ServiceBadgeType;
  badgeLabel?: string;
  durationMinutes: number;
  durationDescription: string;
  description: string;
  tags: string[];
  price: number;
  priceFormatted: string;
  priceNote?: string;
  category: ServiceCategory;
  preparationNotes?: string[];
  targetAudience?: string;
}

export interface ServiceSearchParams {
  query?: string;
  category?: ServiceCategory;
  department?: string;
  priceDurationRange?: string;
  page?: number;
  pageSize?: number;
}

export interface ServiceSearchResponse {
  data: ExaminationService[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

