import type { RoomRecord } from '../types/room-update.types';

export const ROOM_DEPARTMENTS = [
  'Khoa Nội tiết',
  'Khoa Tim mạch',
  'Khoa Nhi',
  'Khoa Ngoại tổng quát',
  'Khoa Chẩn đoán hình ảnh',
] as const;

export const ROOM_EDIT_MOCK: RoomRecord[] = [
  {
    id: 'rm-001',
    code: 'RM-201',
    name: 'Phòng khám Nội tiết 1',
    type: 'OUTPATIENT',
    department: 'Khoa Nội tiết',
    location: 'Nhà A - Tầng 2 - Khu khám ngoại trú',
    capacity: 2,
    status: 'ACTIVE',
  },
  {
    id: 'rm-002',
    code: 'RM-202',
    name: 'Phòng khám Nội tiết 2',
    type: 'OUTPATIENT',
    department: 'Khoa Nội tiết',
    location: 'Nhà A - Tầng 2 - Khu khám ngoại trú',
    capacity: 2,
    status: 'ACTIVE',
  },
  {
    id: 'rm-003',
    code: 'RM-301',
    name: 'Phòng thủ thuật Tim mạch',
    type: 'PROCEDURAL',
    department: 'Khoa Tim mạch',
    location: 'Nhà B - Tầng 3 - Khu thủ thuật',
    capacity: 4,
    status: 'MAINTENANCE',
  },
  {
    id: 'rm-004',
    code: 'RM-105',
    name: 'Phòng khám Nhi 5',
    type: 'OUTPATIENT',
    department: 'Khoa Nhi',
    location: 'Nhà C - Tầng 1 - Khu Nhi',
    capacity: 3,
    status: 'INACTIVE',
  },
];

export function findRoomById(id: string | undefined): RoomRecord | undefined {
  if (!id) return undefined;
  const key = id.toLowerCase();
  return ROOM_EDIT_MOCK.find((r) => r.id.toLowerCase() === key || r.code.toLowerCase() === key);
}
