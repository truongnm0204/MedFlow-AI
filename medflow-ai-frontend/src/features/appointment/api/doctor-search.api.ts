import { MOCK_DOCTORS } from '../data/doctor-search-mock';
import type { DoctorFilterParams, DoctorSearchResponse } from '../types/doctor-search.types';

export async function fetchDoctorsApi(
  params: DoctorFilterParams = {},
): Promise<DoctorSearchResponse> {
  const {
    keyword = '',
    specialty = 'ALL',
    degree = 'ALL',
    hasScheduleToday = false,
    hasScheduleThisWeek = false,
    page = 1,
    pageSize = 6,
  } = params;

  let filtered = [...MOCK_DOCTORS];

  // 1. Keyword search (name, specialty, title, roleTag)
  if (keyword.trim()) {
    const q = keyword.trim().toLowerCase();
    filtered = filtered.filter(
      (doc) =>
        doc.name.toLowerCase().includes(q) ||
        doc.specialty.toLowerCase().includes(q) ||
        doc.roleTag.toLowerCase().includes(q) ||
        doc.title.toLowerCase().includes(q),
    );
  }

  // 2. Specialty category filter
  if (specialty && specialty !== 'ALL') {
    filtered = filtered.filter((doc) => doc.specialtyId === specialty);
  }

  // 3. Academic degree / title filter
  if (degree && degree !== 'ALL') {
    filtered = filtered.filter((doc) => doc.title === degree);
  }

  // 4. Schedule availability filters
  if (hasScheduleToday) {
    filtered = filtered.filter((doc) => doc.hasScheduleToday);
  }

  if (hasScheduleThisWeek) {
    filtered = filtered.filter((doc) => doc.hasScheduleThisWeek);
  }

  const total = filtered.length;
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const safePage = Math.min(Math.max(1, page), totalPages);
  const startIndex = (safePage - 1) * pageSize;
  const items = filtered.slice(startIndex, startIndex + pageSize);

  return {
    items,
    total,
    page: safePage,
    pageSize,
    totalPages,
  };
}
