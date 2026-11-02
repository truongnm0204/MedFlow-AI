import type { AvailableRoomItem } from '../types/assign-room.types';

export const ASSIGN_ROOM_SERVICE = {
  serviceName: 'Khám Chuyên khoa Tiêu Hóa',
  requirement: 'Yêu cầu buồng chuyên khoa tiêu hóa',
} as const;

export const AVAILABLE_ROOMS_MOCK: AvailableRoomItem[] = [
  {
    id: 'room-206',
    code: '206',
    name: 'Phòng 206 - Buồng Tiêu Hóa 02',
    location: 'Tầng 2, Nhà A',
    status: 'READY',
    capacityPerSlot: 2,
    waitingCount: 2,
    compatibilityPercent: 100,
  },
  {
    id: 'room-204',
    code: '204',
    name: 'Phòng 204 - Buồng Tiêu Hóa 01',
    location: 'Tầng 2, Nhà A',
    status: 'BUSY',
    waitingCount: 5,
    compatibilityPercent: 100,
  },
  {
    id: 'room-208',
    code: '208',
    name: 'Phòng 208 - Buồng Tiêu Hóa 03',
    location: 'Tầng 2, Nhà A',
    status: 'OVERLOADED',
    waitingCount: 8,
    compatibilityPercent: 100,
  },
];
