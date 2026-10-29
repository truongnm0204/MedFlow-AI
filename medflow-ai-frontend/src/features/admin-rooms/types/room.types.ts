export type RoomStatus = 'ACTIVE' | 'SUSPENDED' | 'MAINTENANCE';

export interface RoomItem {
  id: string;
  code: string;
  name: string;
  department: string;
  location: string;
  status: RoomStatus;
}

export interface RoomFilters {
  searchQuery: string;
  department: string;
  status: RoomStatus | '';
}

export interface RoomMetrics {
  total: number;
  active: number;
  inactive: number;
  departments: number;
}

export const ROOM_STATUS_LABELS: Record<RoomStatus, string> = {
  ACTIVE: 'Đang hoạt động',
  SUSPENDED: 'Tạm ngừng',
  MAINTENANCE: 'Bảo trì',
};
