import type {
  PriceRangeFilter,
  ServicePricingItem,
  ServicePricingMetrics,
  UpdateServicePricePayload,
} from '../types/service-pricing.types';

export const PRICING_DEPARTMENTS = [
  'Tất cả khoa phòng',
  'Khoa Khám bệnh',
  'Khoa Nội Tiêu hóa',
  'Khoa Tim mạch Can thiệp',
  'Khoa Hô hấp & Dị ứng',
  'Khoa Cơ Xương Khớp',
  'Khoa Thần kinh & Đột quỵ',
  'Khoa Nhi Quốc tế',
  'Khoa Tai Mũi Họng',
  'Khoa Mắt & Khúc xạ',
  'Khoa Da liễu & Thẩm mỹ',
  'Khoa Chẩn đoán Hình ảnh & Thăm dò Chức năng',
] as const;

export const PRICING_CATEGORIES: { label: string; value: string }[] = [
  { label: 'Tất cả phân loại', value: 'all' },
  { label: 'Khám Lâm sàng', value: 'Khám Lâm sàng' },
  { label: 'Hội chẩn Lâm sàng', value: 'Hội chẩn Lâm sàng' },
  { label: 'Cận lâm sàng kỹ thuật', value: 'Cận lâm sàng kỹ thuật' },
];

export const PRICE_RANGE_OPTIONS: { label: string; value: PriceRangeFilter }[] = [
  { label: 'Tất cả mức giá', value: 'all' },
  { label: 'Dưới 200.000 đ', value: 'under-200k' },
  { label: '200.000 đ - 500.000 đ', value: '200k-500k' },
  { label: 'Trên 500.000 đ', value: 'over-500k' },
];

export const PRICING_STATUS_OPTIONS: { label: string; value: string }[] = [
  { label: 'Tất cả trạng thái', value: 'all' },
  { label: 'Đang áp dụng', value: 'ACTIVE' },
  { label: 'Tạm dừng niêm yết', value: 'SUSPENDED' },
];

export const INITIAL_PRICING_DATA: ServicePricingItem[] = [
  {
    id: 'PRC-001',
    code: 'EX-1-01',
    name: 'Khám Nội Tổng quát & Tư vấn Sức khỏe Ban đầu',
    description:
      'Đánh giá các chỉ số sinh hiệu, khám thực thể toàn diện và tư vấn định hướng chuyên khoa',
    category: 'Khám Lâm sàng',
    department: 'Khoa Khám bệnh',
    currentPrice: 300000,
    formattedPrice: '300.000 đ',
    effectiveDate: '2026-01-01',
    decisionNumber: 'QĐ-142/BV-KH',
    status: 'ACTIVE',
    onlineBooking: true,
    roomDefault: 'Phòng 101 - Khám Tổng quát',
    priceHistory: [
      {
        date: '2026-01-01',
        price: 300000,
        decisionNumber: 'QĐ-142/BV-KH',
        note: 'Biểu phí hiện hành áp dụng năm 2026 theo Nghị quyết Hội đồng Quản lý',
        appliedBy: 'Hội đồng Giá Bệnh viện',
      },
      {
        date: '2025-01-01',
        price: 250000,
        decisionNumber: 'QĐ-088/BV-KH',
        note: 'Biểu phí dịch vụ khám ngoại trú năm 2025',
        appliedBy: 'Ban Giám đốc',
      },
      {
        date: '2024-01-01',
        price: 200000,
        decisionNumber: 'QĐ-045/BV-KH',
        note: 'Ban hành biểu giá cơ sở ban đầu',
        appliedBy: 'Ban Giám đốc',
      },
    ],
  },
  {
    id: 'PRC-002',
    code: 'EX-2-02',
    name: 'Khám Chuyên khoa Nội Tiêu hóa & Gan Mật',
    description:
      'Thăm khám, chẩn đoán bệnh lý dạ dày tá tràng, trào ngược thực quản, viêm gan và xơ gan',
    category: 'Khám Lâm sàng',
    department: 'Khoa Nội Tiêu hóa',
    currentPrice: 350000,
    formattedPrice: '350.000 đ',
    effectiveDate: '2026-01-01',
    decisionNumber: 'QĐ-142/BV-KH',
    status: 'ACTIVE',
    onlineBooking: true,
    roomDefault: 'Phòng 204 - Nội Tiêu hóa',
    priceHistory: [
      {
        date: '2026-01-01',
        price: 350000,
        decisionNumber: 'QĐ-142/BV-KH',
        note: 'Cập nhật định mức thù lao chuyên gia tiêu hóa năm 2026',
        appliedBy: 'Hội đồng Giá Bệnh viện',
      },
      {
        date: '2025-01-01',
        price: 300000,
        decisionNumber: 'QĐ-088/BV-KH',
        note: 'Biểu phí khám tiêu hóa 2025',
        appliedBy: 'Ban Giám đốc',
      },
    ],
  },
  {
    id: 'PRC-003',
    code: 'EX-2-03',
    name: 'Khám Tim mạch Can thiệp & Đo Điện tim Chuyên sâu',
    description:
      'Tầm soát rối loạn nhịp tim, bệnh mạch vành, tăng huyết áp kháng trị kèm đo ECG 12 chuyển đạo',
    category: 'Khám Lâm sàng',
    department: 'Khoa Tim mạch Can thiệp',
    currentPrice: 400000,
    formattedPrice: '400.000 đ',
    effectiveDate: '2026-01-01',
    decisionNumber: 'QĐ-142/BV-KH',
    status: 'ACTIVE',
    onlineBooking: true,
    roomDefault: 'Phòng 208 - Tim mạch',
    priceHistory: [
      {
        date: '2026-01-01',
        price: 400000,
        decisionNumber: 'QĐ-142/BV-KH',
        note: 'Áp dụng khung giá khám tim mạch can thiệp năm 2026',
        appliedBy: 'Hội đồng Giá Bệnh viện',
      },
      {
        date: '2025-01-01',
        price: 350000,
        decisionNumber: 'QĐ-088/BV-KH',
        note: 'Biểu phí khám tim mạch 2025',
        appliedBy: 'Ban Giám đốc',
      },
    ],
  },
  {
    id: 'PRC-004',
    code: 'EX-2-04',
    name: 'Khám Hô hấp & Đánh giá Chức năng Phổi',
    description: 'Chẩn đoán hen phế quản, COPD, viêm đường hô hấp mạn tính và tư vấn cai thuốc lá',
    category: 'Khám Lâm sàng',
    department: 'Khoa Hô hấp & Dị ứng',
    currentPrice: 320000,
    formattedPrice: '320.000 đ',
    effectiveDate: '2026-01-01',
    decisionNumber: 'QĐ-142/BV-KH',
    status: 'ACTIVE',
    onlineBooking: true,
    roomDefault: 'Phòng 205 - Khám Hô hấp',
    priceHistory: [
      {
        date: '2026-01-01',
        price: 320000,
        decisionNumber: 'QĐ-142/BV-KH',
        note: 'Biểu phí khám hô hấp năm 2026',
        appliedBy: 'Hội đồng Giá Bệnh viện',
      },
    ],
  },
  {
    id: 'PRC-005',
    code: 'EX-2-05',
    name: 'Khám Cơ Xương Khớp & Tư vấn Phục hồi',
    description:
      'Khám thoái hóa khớp, thoát vị đĩa đệm, gout, viêm khớp dạng thấp và tiêm nội khớp',
    category: 'Khám Lâm sàng',
    department: 'Khoa Cơ Xương Khớp',
    currentPrice: 320000,
    formattedPrice: '320.000 đ',
    effectiveDate: '2026-01-01',
    decisionNumber: 'QĐ-142/BV-KH',
    status: 'ACTIVE',
    onlineBooking: true,
    roomDefault: 'Phòng 206 - Cơ Xương Khớp',
    priceHistory: [
      {
        date: '2026-01-01',
        price: 320000,
        decisionNumber: 'QĐ-142/BV-KH',
        note: 'Biểu phí áp dụng năm 2026',
        appliedBy: 'Hội đồng Giá Bệnh viện',
      },
    ],
  },
  {
    id: 'PRC-006',
    code: 'EX-2-06',
    name: 'Khám Chuyên khoa Thần kinh & Tầm soát Sa sút Trí tuệ',
    description:
      'Đánh giá đau đầu mạn tính, rối loạn giấc ngủ, bệnh Parkinson và di chứng tai biến mạch máu não',
    category: 'Khám Lâm sàng',
    department: 'Khoa Thần kinh & Đột quỵ',
    currentPrice: 380000,
    formattedPrice: '380.000 đ',
    effectiveDate: '2026-01-01',
    decisionNumber: 'QĐ-142/BV-KH',
    status: 'ACTIVE',
    onlineBooking: true,
    roomDefault: 'Phòng 207 - Thần kinh',
    priceHistory: [
      {
        date: '2026-01-01',
        price: 380000,
        decisionNumber: 'QĐ-142/BV-KH',
        note: 'Biểu phí khám thần kinh năm 2026',
        appliedBy: 'Hội đồng Giá Bệnh viện',
      },
      {
        date: '2025-01-01',
        price: 330000,
        decisionNumber: 'QĐ-088/BV-KH',
        note: 'Biểu phí khám thần kinh 2025',
        appliedBy: 'Ban Giám đốc',
      },
    ],
  },
  {
    id: 'PRC-007',
    code: 'EX-3-01',
    name: 'Khám Nhi Tổng quát & Đánh giá Dinh dưỡng',
    description:
      'Khám sức khỏe tổng quát trẻ em, tư vấn tiêm chủng, theo dõi biểu đồ phát triển thể chất',
    category: 'Khám Lâm sàng',
    department: 'Khoa Nhi Quốc tế',
    currentPrice: 280000,
    formattedPrice: '280.000 đ',
    effectiveDate: '2026-01-01',
    decisionNumber: 'QĐ-142/BV-KH',
    status: 'ACTIVE',
    onlineBooking: true,
    roomDefault: 'Phòng 301 - Nhi Tổng quát',
    priceHistory: [
      {
        date: '2026-01-01',
        price: 280000,
        decisionNumber: 'QĐ-142/BV-KH',
        note: 'Biểu phí khám nhi năm 2026',
        appliedBy: 'Hội đồng Giá Bệnh viện',
      },
    ],
  },
  {
    id: 'PRC-008',
    code: 'EX-3-02',
    name: 'Khám Tai Mũi Họng & Nội soi Ống mềm',
    description:
      'Nội soi vòm họng, thanh quản phát hiện sớm polyp, viêm xoang mạn, tầm soát ung thư vòm',
    category: 'Khám Lâm sàng',
    department: 'Khoa Tai Mũi Họng',
    currentPrice: 350000,
    formattedPrice: '350.000 đ',
    effectiveDate: '2026-01-01',
    decisionNumber: 'QĐ-142/BV-KH',
    status: 'ACTIVE',
    onlineBooking: true,
    roomDefault: 'Phòng 304 - Tai Mũi Họng',
    priceHistory: [
      {
        date: '2026-01-01',
        price: 350000,
        decisionNumber: 'QĐ-142/BV-KH',
        note: 'Biểu phí gói khám kèm nội soi năm 2026',
        appliedBy: 'Hội đồng Giá Bệnh viện',
      },
    ],
  },
  {
    id: 'PRC-009',
    code: 'EX-3-03',
    name: 'Khám Mắt Toàn diện & Đo Khúc xạ Máy Tự động',
    description: 'Đo thị lực chuyên sâu, soi đáy mắt, đo nhãn áp phát hiện glaucome và tật khúc xạ',
    category: 'Khám Lâm sàng',
    department: 'Khoa Mắt & Khúc xạ',
    currentPrice: 250000,
    formattedPrice: '250.000 đ',
    effectiveDate: '2026-01-01',
    decisionNumber: 'QĐ-142/BV-KH',
    status: 'ACTIVE',
    onlineBooking: true,
    roomDefault: 'Phòng 307 - Mắt',
    priceHistory: [
      {
        date: '2026-01-01',
        price: 250000,
        decisionNumber: 'QĐ-142/BV-KH',
        note: 'Biểu phí đo khám mắt năm 2026',
        appliedBy: 'Hội đồng Giá Bệnh viện',
      },
    ],
  },
  {
    id: 'PRC-010',
    code: 'EX-3-04',
    name: 'Khám Da liễu & Soi Da Dermoscopy',
    description:
      'Chẩn đoán viêm da dị ứng, mụn trứng cá, nám sạm và tầm soát u da ác tính bằng dermoscopy',
    category: 'Khám Lâm sàng',
    department: 'Khoa Da liễu & Thẩm mỹ',
    currentPrice: 300000,
    formattedPrice: '300.000 đ',
    effectiveDate: '2026-01-01',
    decisionNumber: 'QĐ-142/BV-KH',
    status: 'ACTIVE',
    onlineBooking: true,
    roomDefault: 'Phòng 310 - Da liễu',
    priceHistory: [
      {
        date: '2026-01-01',
        price: 300000,
        decisionNumber: 'QĐ-142/BV-KH',
        note: 'Biểu phí khám da liễu năm 2026',
        appliedBy: 'Hội đồng Giá Bệnh viện',
      },
    ],
  },
  {
    id: 'PRC-011',
    code: 'HC-1-01',
    name: 'Hội chẩn Chuyên gia Đa chuyên khoa (Complex Case Consultation)',
    description:
      'Phiên hội chẩn liên viện giữa Giáo sư/Tiến sĩ các chuyên khoa cho ca bệnh phức tạp',
    category: 'Hội chẩn Lâm sàng',
    department: 'Khoa Khám bệnh',
    currentPrice: 1200000,
    formattedPrice: '1.200.000 đ',
    effectiveDate: '2026-01-01',
    decisionNumber: 'QĐ-142/BV-KH',
    status: 'ACTIVE',
    onlineBooking: false,
    roomDefault: 'Phòng Hội đồng Chuyên môn Tầng 5',
    priceHistory: [
      {
        date: '2026-01-01',
        price: 1200000,
        decisionNumber: 'QĐ-142/BV-KH',
        note: 'Mức giá hội chẩn chuyên gia cấp bệnh viện',
        appliedBy: 'Hội đồng Giá Bệnh viện',
      },
      {
        date: '2025-01-01',
        price: 1000000,
        decisionNumber: 'QĐ-088/BV-KH',
        note: 'Biểu phí hội chẩn năm 2025',
        appliedBy: 'Ban Giám đốc',
      },
    ],
  },
  {
    id: 'PRC-012',
    code: 'CLS-US-01',
    name: 'Siêu âm Doppler Tim & Mạch máu Chuyên sâu',
    description:
      'Khảo sát cấu trúc buồng tim, huyết động qua van, đo phân suất tống máu EF bằng máy 4D',
    category: 'Cận lâm sàng kỹ thuật',
    department: 'Khoa Chẩn đoán Hình ảnh & Thăm dò Chức năng',
    currentPrice: 450000,
    formattedPrice: '450.000 đ',
    effectiveDate: '2026-01-01',
    decisionNumber: 'QĐ-142/BV-KH',
    status: 'ACTIVE',
    onlineBooking: true,
    roomDefault: 'Phòng Siêu âm Tim 102',
    priceHistory: [
      {
        date: '2026-01-01',
        price: 450000,
        decisionNumber: 'QĐ-142/BV-KH',
        note: 'Biểu phí siêu âm tim Doppler màu năm 2026',
        appliedBy: 'Hội đồng Giá Bệnh viện',
      },
      {
        date: '2025-01-01',
        price: 400000,
        decisionNumber: 'QĐ-088/BV-KH',
        note: 'Biểu phí năm 2025',
        appliedBy: 'Ban Giám đốc',
      },
    ],
  },
  {
    id: 'PRC-013',
    code: 'CLS-US-02',
    name: 'Siêu âm Ổ bụng Tổng quát Màu 4D',
    description:
      'Đánh giá gan, mật, tụy, lách, thận, bàng quang, tiền liệt tuyến hoặc tử cung phần phụ',
    category: 'Cận lâm sàng kỹ thuật',
    department: 'Khoa Chẩn đoán Hình ảnh & Thăm dò Chức năng',
    currentPrice: 180000,
    formattedPrice: '180.000 đ',
    effectiveDate: '2026-01-01',
    decisionNumber: 'QĐ-142/BV-KH',
    status: 'ACTIVE',
    onlineBooking: true,
    roomDefault: 'Phòng Siêu âm Tổng quát 104',
    priceHistory: [
      {
        date: '2026-01-01',
        price: 180000,
        decisionNumber: 'QĐ-142/BV-KH',
        note: 'Biểu phí siêu âm bụng tổng quát',
        appliedBy: 'Hội đồng Giá Bệnh viện',
      },
    ],
  },
  {
    id: 'PRC-014',
    code: 'CLS-ECG-01',
    name: 'Ghi Điện tim đồ 12 Chuyển đạo Kỹ thuật số (Digital ECG)',
    description:
      'Đo dòng điện sinh học của cơ tim, phân tích tự động hỗ trợ chẩn đoán thiếu máu cục bộ',
    category: 'Cận lâm sàng kỹ thuật',
    department: 'Khoa Chẩn đoán Hình ảnh & Thăm dò Chức năng',
    currentPrice: 80000,
    formattedPrice: '80.000 đ',
    effectiveDate: '2026-01-01',
    decisionNumber: 'QĐ-142/BV-KH',
    status: 'ACTIVE',
    onlineBooking: true,
    roomDefault: 'Phòng Thăm dò Điện tim 106',
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
    id: 'PRC-015',
    code: 'CLS-ENDO-01',
    name: 'Nội soi Dạ dày - Tá tràng Không đau (Gây mê An thần)',
    description:
      'Quan sát niêm mạc thực quản, dạ dày, tá tràng có an thần nhẹ, test HP qua mẫu sinh thiết',
    category: 'Cận lâm sàng kỹ thuật',
    department: 'Khoa Nội Tiêu hóa',
    currentPrice: 1500000,
    formattedPrice: '1.500.000 đ',
    effectiveDate: '2026-01-01',
    decisionNumber: 'QĐ-142/BV-KH',
    status: 'ACTIVE',
    onlineBooking: true,
    roomDefault: 'Phòng Can thiệp Nội soi Tiêu hóa P202',
    priceHistory: [
      {
        date: '2026-01-01',
        price: 1500000,
        decisionNumber: 'QĐ-142/BV-KH',
        note: 'Biểu phí nội soi có gây mê kèm test vi khuẩn HP',
        appliedBy: 'Hội đồng Giá Bệnh viện',
      },
      {
        date: '2025-01-01',
        price: 1300000,
        decisionNumber: 'QĐ-088/BV-KH',
        note: 'Biểu phí nội soi 2025',
        appliedBy: 'Ban Giám đốc',
      },
    ],
  },
  {
    id: 'PRC-016',
    code: 'EX-VIP-01',
    name: 'Khám Sức khỏe Tiền Hôn nhân & Tư vấn Di truyền VIP',
    description: 'Gói khám tầm soát toàn diện tiền sản, xét nghiệm gen và tư vấn bác sĩ cao cấp',
    category: 'Khám Lâm sàng',
    department: 'Khoa Khám bệnh',
    currentPrice: 850000,
    formattedPrice: '850.000 đ',
    effectiveDate: '2025-07-01',
    decisionNumber: 'QĐ-088/BV-KH',
    status: 'SUSPENDED',
    onlineBooking: false,
    roomDefault: 'Phòng VIP Khu A',
    priceHistory: [
      {
        date: '2025-07-01',
        price: 850000,
        decisionNumber: 'QĐ-088/BV-KH',
        note: 'Tạm dừng tiếp nhận theo yêu cầu tái cấu trúc dịch vụ',
        appliedBy: 'Ban Giám đốc',
      },
    ],
  },
];

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
  const averagePrice = Math.round(sum / totalServices);
  const highestPrice = Math.max(...prices);
  const lowestPrice = Math.min(...prices);

  return {
    totalServices,
    activePricingCount,
    suspendedCount,
    averagePrice,
    highestPrice,
    lowestPrice,
  };
}

export function updateMockServicePrice(
  id: string,
  payload: UpdateServicePricePayload,
): ServicePricingItem | null {
  const targetIndex = INITIAL_PRICING_DATA.findIndex((i) => i.id === id || i.code === id);
  if (targetIndex === -1) return null;

  const existing = INITIAL_PRICING_DATA[targetIndex];
  const newHistoryRecord = {
    date: payload.effectiveDate,
    price: payload.newPrice,
    decisionNumber: payload.decisionNumber,
    note: payload.note || 'Cập nhật biểu phí theo quyết định mới',
    appliedBy: payload.appliedBy,
  };

  const updatedItem: ServicePricingItem = {
    ...existing,
    currentPrice: payload.newPrice,
    formattedPrice: `${payload.newPrice.toLocaleString('vi-VN')} đ`,
    effectiveDate: payload.effectiveDate,
    decisionNumber: payload.decisionNumber,
    status: payload.status,
    priceHistory: [newHistoryRecord, ...(existing.priceHistory || [])],
  };

  INITIAL_PRICING_DATA[targetIndex] = updatedItem;
  return updatedItem;
}

const BASE_PRICING_BACKUP: ServicePricingItem[] = JSON.parse(JSON.stringify(INITIAL_PRICING_DATA));

export function resetMockServicePricing(): void {
  INITIAL_PRICING_DATA.length = 0;
  INITIAL_PRICING_DATA.push(...JSON.parse(JSON.stringify(BASE_PRICING_BACKUP)));
}
