import type { Hospital } from '../types/booking-entry.types';

/** Mock cơ sở khám dùng khi `GET /api/v1/hospitals` chưa tồn tại (ADR-0006). */
export const MOCK_HOSPITALS: Hospital[] = [
  {
    id: 'hos-01',
    hospitalCode: 'HOS-HN-01',
    name: 'Bệnh viện MedFlow — Cơ sở Hà Nội',
    address: 'Số 1 Đường Giải Phóng, Phương Mai, Đống Đa, Hà Nội',
    isActive: true,
  },
  {
    id: 'hos-02',
    hospitalCode: 'HOS-HCM-01',
    name: 'Bệnh viện MedFlow — Cơ sở TP. Hồ Chí Minh',
    address: '215 Hồng Bàng, Phường 11, Quận 5, TP. Hồ Chí Minh',
    isActive: true,
  },
  {
    id: 'hos-03',
    hospitalCode: 'HOS-DN-01',
    name: 'Bệnh viện MedFlow — Cơ sở Đà Nẵng',
    address: '124 Hải Phòng, Thạch Thang, Hải Châu, Đà Nẵng',
    isActive: false,
  },
];
