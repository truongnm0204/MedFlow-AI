import { apiClient } from '@/shared/lib/axios';

import type { Hospital, HospitalDto } from '../types/booking-entry.types';

function mapHospitalDto(dto: HospitalDto): Hospital {
  return {
    id: String(dto.id),
    hospitalCode: dto.hospitalCode ?? '',
    name: dto.name ?? 'Cơ sở chưa đặt tên',
    address: dto.address ?? '',
    isActive: dto.isActive === true,
  };
}

export async function fetchHospitalsApi(): Promise<Hospital[]> {
  const { data } = await apiClient.get<HospitalDto[]>('/hospitals');
  return data.map(mapHospitalDto);
}
