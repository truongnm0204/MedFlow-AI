import type {
  ServicePricingItem,
  ServicePricingMetrics,
  UpdateServicePricePayload,
} from '../types/service-pricing.types';

export const PRICING_DEPARTMENTS = [
  'Tất cả khoa phòng',
  'Khoa Khám bệnh',
  'Khoa Nội Tiêu hóa',
  'Khoa Tim mạch Can thiệp',
  'Khoa Chẩn đoán Hình ảnh',
] as const;

export const PRICING_CATEGORIES = [
  { label: 'Tất cả phân loại', value: 'all' },
  { label: 'Khám Lâm sàng', value: 'Khám Lâm sàng' },
  { label: 'Hội chẩn Lâm sàng', value: 'Hội chẩn Lâm sàng' },
  { label: 'Cận lâm sàng kỹ thuật', value: 'Cận lâm sàng kỹ thuật' },
];

export const INITIAL_PRICING_DATA: ServicePricingItem[] = [
  {
    id: 'PRC-001',
    code: 'EX-1-01',
    name: 'Khám Nội Tổng quát & Tư vấn Sức khỏe Ban đầu',
    description: 'Đánh giá chỉ số sinh hiệu và khám thực thể toàn diện',
    category: 'Khám Lâm sàng',
    department: 'Khoa Khám bệnh',
    currentPrice: 300000,
    formattedPrice: '300.000 đ',
    effectiveDate: '2026-01-01',
    decisionNumber: 'QĐ-142/BV-KH',
    status: 'ACTIVE',
    onlineBooking: true,
    priceHistory: [
      {
        date: '2026-01-01',
        price: 300000,
        decisionNumber: 'QĐ-142/BV-KH',
        note: 'Biểu phí hiện hành áp dụng năm 2026',
        appliedBy: 'Hội đồng Giá Bệnh viện',
      },
      {
        date: '2025-01-01',
        price: 250000,
        decisionNumber: 'QĐ-088/BV-KH',
        note: 'Biểu phí ban hành năm 2025',
        appliedBy: 'Ban Giám đốc',
      },
    ],
  },
  {
    id: 'PRC-002',
    code: 'EX-2-02',
    name: 'Khám Chuyên khoa Nội Tiêu hóa & Gan Mật',
    description: 'Thăm khám, chẩn đoán bệnh lý dạ dày tá tràng, gan mật',
    category: 'Khám Lâm sàng',
    department: 'Khoa Nội Tiêu hóa',
    currentPrice: 350000,
    formattedPrice: '350.000 đ',
    effectiveDate: '2026-01-01',
    decisionNumber: 'QĐ-142/BV-KH',
    status: 'ACTIVE',
    onlineBooking: true,
    priceHistory: [
      {
        date: '2026-01-01',
        price: 350000,
        decisionNumber: 'QĐ-142/BV-KH',
        note: 'Biểu phí áp dụng năm 2026',
        appliedBy: 'Hội đồng Giá Bệnh viện',
      },
    ],
  },
  {
    id: 'PRC-003',
    code: 'EX-2-03',
    name: 'Khám Tim mạch Can thiệp & Đo Điện tim Chuyên sâu',
    description: 'Tầm soát rối loạn nhịp tim, bệnh mạch vành, đo ECG',
    category: 'Khám Lâm sàng',
    department: 'Khoa Tim mạch Can thiệp',
    currentPrice: 400000,
    formattedPrice: '400.000 đ',
    effectiveDate: '2026-01-01',
    decisionNumber: 'QĐ-142/BV-KH',
    status: 'ACTIVE',
    onlineBooking: true,
    priceHistory: [
      {
        date: '2026-01-01',
        price: 400000,
        decisionNumber: 'QĐ-142/BV-KH',
        note: 'Biểu phí khám tim mạch năm 2026',
        appliedBy: 'Hội đồng Giá Bệnh viện',
      },
    ],
  },
  {
    id: 'PRC-004',
    code: 'HC-1-01',
    name: 'Hội chẩn Chuyên gia Đa chuyên khoa (Complex Case Consultation)',
    description: 'Phiên hội chẩn liên viện giữa Giáo sư/Tiến sĩ đa chuyên khoa',
    category: 'Hội chẩn Lâm sàng',
    department: 'Khoa Khám bệnh',
    currentPrice: 1200000,
    formattedPrice: '1.200.000 đ',
    effectiveDate: '2026-01-01',
    decisionNumber: 'QĐ-142/BV-KH',
    status: 'ACTIVE',
    onlineBooking: false,
    priceHistory: [
      {
        date: '2026-01-01',
        price: 1200000,
        decisionNumber: 'QĐ-142/BV-KH',
        note: 'Biểu phí hội chẩn chuyên gia cấp bệnh viện',
        appliedBy: 'Hội đồng Giá Bệnh viện',
      },
    ],
  },
  {
    id: 'PRC-005',
    code: 'CLS-US-01',
    name: 'Siêu âm Doppler Tim & Mạch máu Chuyên sâu',
    description: 'Khảo sát cấu trúc tim, đo phân suất tống máu EF bằng máy 4D',
    category: 'Cận lâm sàng kỹ thuật',
    department: 'Khoa Chẩn đoán Hình ảnh',
    currentPrice: 450000,
    formattedPrice: '450.000 đ',
    effectiveDate: '2026-01-01',
    decisionNumber: 'QĐ-142/BV-KH',
    status: 'ACTIVE',
    onlineBooking: true,
    priceHistory: [
      {
        date: '2026-01-01',
        price: 450000,
        decisionNumber: 'QĐ-142/BV-KH',
        note: 'Biểu phí siêu âm Doppler màu năm 2026',
        appliedBy: 'Hội đồng Giá Bệnh viện',
      },
    ],
  },
  {
    id: 'PRC-006',
    code: 'CLS-ECG-01',
    name: 'Ghi Điện tim đồ 12 Chuyển đạo Kỹ thuật số (Digital ECG)',
    description: 'Đo dòng điện sinh học tim, phân tích tự động hỗ trợ chẩn đoán',
    category: 'Cận lâm sàng kỹ thuật',
    department: 'Khoa Chẩn đoán Hình ảnh',
    currentPrice: 80000,
    formattedPrice: '80.000 đ',
    effectiveDate: '2026-01-01',
    decisionNumber: 'QĐ-142/BV-KH',
    status: 'ACTIVE',
    onlineBooking: true,
    priceHistory: [
      {
        date: '2026-01-01',
        price: 80000,
        decisionNumber: 'QĐ-142/BV-KH',
        note: 'Biểu phí điện tim kỹ thuật số',
        appliedBy: 'Hội đồng Giá Bệnh viện',
      },
    ],
  },
  {
    id: 'PRC-007',
    code: 'EX-VIP-01',
    name: 'Khám Sức khỏe Tiền Hôn nhân & Tư vấn Di truyền VIP',
    description: 'Gói khám toàn diện tiền hôn nhân và tư vấn bác sĩ cao cấp',
    category: 'Khám Lâm sàng',
    department: 'Khoa Khám bệnh',
    currentPrice: 850000,
    formattedPrice: '850.000 đ',
    effectiveDate: '2025-07-01',
    decisionNumber: 'QĐ-088/BV-KH',
    status: 'SUSPENDED',
    onlineBooking: false,
    priceHistory: [
      {
        date: '2025-07-01',
        price: 850000,
        decisionNumber: 'QĐ-088/BV-KH',
        note: 'Tạm dừng niêm yết theo kế hoạch tái cấu trúc gói',
        appliedBy: 'Ban Giám đốc',
      },
    ],
  },
];

const BACKUP_PRICING_DATA: ServicePricingItem[] = JSON.parse(JSON.stringify(INITIAL_PRICING_DATA));

export function resetMockServicePricing(): void {
  INITIAL_PRICING_DATA.length = 0;
  INITIAL_PRICING_DATA.push(...JSON.parse(JSON.stringify(BACKUP_PRICING_DATA)));
}

export function calculateMetrics(items: ServicePricingItem[]): ServicePricingMetrics {
  const totalServices = items.length;
  const activePricingCount = items.filter((i) => i.status === 'ACTIVE').length;
  const suspendedCount = items.filter((i) => i.status === 'SUSPENDED').length;

  if (totalServices === 0) {
    return {
      totalServices: 0,
      activePricingCount: 0,
      suspendedCount: 0,
      averagePrice: 0,
      highestPrice: 0,
      lowestPrice: 0,
    };
  }

  const prices = items.map((i) => i.currentPrice);
  const sum = prices.reduce((acc, p) => acc + p, 0);
  return {
    totalServices,
    activePricingCount,
    suspendedCount,
    averagePrice: Math.round(sum / totalServices),
    highestPrice: Math.max(...prices),
    lowestPrice: Math.min(...prices),
  };
}

export function updateMockServicePrice(
  id: string,
  payload: UpdateServicePricePayload,
): ServicePricingItem | null {
  const index = INITIAL_PRICING_DATA.findIndex((i) => i.id === id || i.code === id);
  if (index === -1) return null;

  const existing = INITIAL_PRICING_DATA[index];
  const updatedItem: ServicePricingItem = {
    ...existing,
    currentPrice: payload.newPrice,
    formattedPrice: `${payload.newPrice.toLocaleString('vi-VN')} đ`,
    effectiveDate: payload.effectiveDate,
    decisionNumber: payload.decisionNumber,
    status: payload.status,
    priceHistory: [
      {
        date: payload.effectiveDate,
        price: payload.newPrice,
        decisionNumber: payload.decisionNumber,
        note: payload.note || 'Cập nhật biểu phí theo quyết định mới',
        appliedBy: payload.appliedBy,
      },
      ...(existing.priceHistory || []),
    ],
  };

  INITIAL_PRICING_DATA[index] = updatedItem;
  return updatedItem;
}
