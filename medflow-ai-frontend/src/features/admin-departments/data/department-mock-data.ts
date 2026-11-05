import type { DepartmentItem } from '../types/department.types';

export const DEFAULT_FACILITY = 'MedFlow Cơ sở 1';

interface DepartmentMockMeta {
  name: string;
  active: boolean;
  rooms?: number;
  doctors?: number;
  services?: number;
  pendingAppointments?: number;
}

const MOCK_DEPT_CONFIGS: DepartmentMockMeta[] = [
  {
    name: 'Khoa Nội Tiêu hóa',
    active: true,
    rooms: 2,
    doctors: 3,
    services: 2,
    pendingAppointments: 4,
  },
  {
    name: 'Khoa Khám bệnh',
    active: true,
    rooms: 4,
    doctors: 5,
    services: 1,
    pendingAppointments: 2,
  },
  {
    name: 'Khoa Nội tiết',
    active: true,
    rooms: 0,
    doctors: 0,
    services: 0,
    pendingAppointments: 0,
  },
  {
    name: 'Khoa Chẩn đoán hình ảnh (CĐHA)',
    active: true,
    rooms: 3,
    doctors: 2,
    services: 2,
    pendingAppointments: 1,
  },
  {
    name: 'Khoa Xét nghiệm',
    active: true,
    rooms: 2,
    doctors: 2,
    services: 1,
    pendingAppointments: 0,
  },
  {
    name: 'Khoa Tim mạch',
    active: true,
    rooms: 2,
    doctors: 3,
    services: 2,
    pendingAppointments: 3,
  },
  { name: 'Khoa Nhi', active: true, rooms: 1, doctors: 2, services: 1, pendingAppointments: 0 },
  { name: 'Khoa Sản', active: true, rooms: 2, doctors: 2, services: 1, pendingAppointments: 0 },
  {
    name: 'Khoa Ngoại tổng hợp',
    active: true,
    rooms: 2,
    doctors: 2,
    services: 1,
    pendingAppointments: 1,
  },
  {
    name: 'Khoa Tai Mũi Họng',
    active: true,
    rooms: 1,
    doctors: 1,
    services: 1,
    pendingAppointments: 0,
  },
  {
    name: 'Khoa Răng Hàm Mặt',
    active: true,
    rooms: 0,
    doctors: 0,
    services: 0,
    pendingAppointments: 0,
  },
  { name: 'Khoa Mắt', active: true, rooms: 0, doctors: 0, services: 0, pendingAppointments: 0 },
  {
    name: 'Khoa Da liễu',
    active: false,
    rooms: 0,
    doctors: 0,
    services: 0,
    pendingAppointments: 0,
  },
  {
    name: 'Khoa Y học cổ truyền',
    active: false,
    rooms: 0,
    doctors: 0,
    services: 0,
    pendingAppointments: 0,
  },
];

export const DEPARTMENT_MOCK_DATA: DepartmentItem[] = MOCK_DEPT_CONFIGS.map((config, index) => {
  const n = String(index + 1).padStart(3, '0');
  const hasDependencies =
    (config.rooms ?? 0) > 0 || (config.doctors ?? 0) > 0 || (config.pendingAppointments ?? 0) > 0;

  return {
    id: `dep-${n}`,
    code: `DEP-${n}`,
    name: config.name,
    facility: DEFAULT_FACILITY,
    status: config.active ? 'ACTIVE' : 'INACTIVE',
    activeRoomsCount: config.rooms ?? 0,
    activeDoctorsCount: config.doctors ?? 0,
    activeServicesCount: config.services ?? 0,
    pendingAppointmentsCount: config.pendingAppointments ?? 0,
    hasActiveDependencies: config.active ? hasDependencies : false,
  };
});
