import { MOCK_EXAMINATION_SERVICES } from '../data/service-search-mock';
import type {
  ExaminationService,
  ServiceSearchParams,
  ServiceSearchResponse,
} from '../types/service-search.types';

export async function searchExaminationServicesApi(
  params: ServiceSearchParams = {},
): Promise<ServiceSearchResponse> {
  // Giả lập độ trễ mạng thực tế
  await new Promise((resolve) => setTimeout(resolve, 150));

  let filtered: ExaminationService[] = [...MOCK_EXAMINATION_SERVICES];

  // 1. Lọc theo từ khóa tìm kiếm (query)
  if (params.query?.trim()) {
    const q = params.query.trim().toLowerCase();
    filtered = filtered.filter(
      (s) =>
        s.name.toLowerCase().includes(q) ||
        s.serviceCode.toLowerCase().includes(q) ||
        s.departmentName.toLowerCase().includes(q) ||
        s.description.toLowerCase().includes(q) ||
        s.tags.some((tag) => tag.toLowerCase().includes(q)),
    );
  }

  // 2. Lọc theo phân loại (category)
  if (params.category && params.category !== 'all') {
    filtered = filtered.filter((s) => s.category === params.category);
  }

  // 3. Lọc theo khoa phòng (department)
  if (params.department && params.department !== 'all') {
    filtered = filtered.filter(
      (s) => s.departmentId === params.department || s.departmentName === params.department,
    );
  }

  // 4. Lọc theo mức giá / thời lượng khám (priceDurationRange)
  if (params.priceDurationRange && params.priceDurationRange !== 'all') {
    switch (params.priceDurationRange) {
      case 'under-200k':
        filtered = filtered.filter((s) => s.price <= 200000);
        break;
      case '200k-250k':
        filtered = filtered.filter((s) => s.price > 200000 && s.price <= 250000);
        break;
      case 'over-250k':
        filtered = filtered.filter((s) => s.price > 250000);
        break;
      case 'duration-under-20':
        filtered = filtered.filter((s) => s.durationMinutes <= 20);
        break;
      case 'duration-over-20':
        filtered = filtered.filter((s) => s.durationMinutes > 20);
        break;
      default:
        break;
    }
  }

  const total = filtered.length;
  const page = Math.max(1, params.page || 1);
  const pageSize = Math.max(1, params.pageSize || 6);
  const totalPages = Math.ceil(total / pageSize) || 1;

  const startIndex = (page - 1) * pageSize;
  const paginatedData = filtered.slice(startIndex, startIndex + pageSize);

  return {
    data: paginatedData,
    total,
    page,
    pageSize,
    totalPages,
  };
}

export async function getExaminationServiceDetailApi(
  serviceId: string,
): Promise<ExaminationService | null> {
  await new Promise((resolve) => setTimeout(resolve, 100));
  const service = MOCK_EXAMINATION_SERVICES.find((s) => s.id === serviceId);
  return service || null;
}
