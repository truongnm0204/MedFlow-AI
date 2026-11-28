export interface MedicinePriceHistoryEntry {
  id: number;
  priceAmount: number;
  priceFormatted: string;
  validFrom: string | null;
  validTo: string | null;
  date: string;
  decisionNumber: string | null;
  note: string | null;
  appliedBy: string | null;
  isActive: boolean;
  status: string;
}

export interface MedicinePricingMetrics {
  totalMedicines: number;
  pricedMedicinesCount: number;
  pricedRatioPercent: number;
  averagePrice: number;
  minPrice: number;
  maxPrice: number;
  latestEffectiveDate: string | null;
}

export interface MedicinePriceItem {
  id: number;
  medicineCode: string | null;
  name: string;
  activeIngredient: string | null;
  strength: string | null;
  dosageForm: string | null;
  dispensingUnit: string | null;
  hospitalId: number | null;
  facility: string;
  isActive: boolean;
  status: string;
  price: number;
  currentPriceAmount: number | null;
  hasEffectivePrice: boolean;
  priceFormatted: string;
  validFrom: string | null;
  validTo: string | null;
  effectiveDate: string | null;
  decisionNumber: string | null;
  appliedBy: string | null;
  note: string | null;
  priceHistory: MedicinePriceHistoryEntry[];
}

export interface MedicinePriceListResult {
  hospitalId: number | null;
  hospitalName: string | null;
  data: MedicinePriceItem[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
  metrics: MedicinePricingMetrics;
}

export interface MedicinePriceDetail {
  medicineId: number;
  medicineCode: string | null;
  name: string;
  activeIngredient: string | null;
  strength: string | null;
  dosageForm: string | null;
  dispensingUnit: string | null;
  hospitalId: number | null;
  facility: string;
  isActive: boolean;
  status: string;
  price: number;
  currentPriceAmount: number | null;
  hasEffectivePrice: boolean;
  priceFormatted: string;
  validFrom: string | null;
  validTo: string | null;
  effectiveDate: string | null;
  decisionNumber: string | null;
  appliedBy: string | null;
  note: string | null;
  priceHistory: MedicinePriceHistoryEntry[];
}

export interface MedicinePriceHistoryResult {
  medicineId: number;
  medicineCode: string | null;
  name: string;
  hospitalId: number | null;
  priceHistory: MedicinePriceHistoryEntry[];
}

export interface UpdateMedicinePricePayload {
  medicineId?: number | null;
  medicineCode?: string | null;
  priceAmount?: number | null;
  price?: number | null;
  validFrom?: string | null;
  effectiveDate?: string | null;
  validTo?: string | null;
  decisionNumber?: string | null;
  appliedBy?: string | null;
  note?: string | null;
  reason?: string | null;
}

export interface MedicinePriceListParams {
  page?: number;
  pageSize?: number;
  keyword?: string;
  medicineId?: number;
  hospitalId?: number;
  isActive?: boolean;
  hasEffectivePrice?: boolean;
  effectiveAt?: string;
}
