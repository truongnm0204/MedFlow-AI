export type RoomStatus = 'ACTIVE' | 'INACTIVE' | 'MAINTENANCE';

export interface RoomItem {
  id: string;
  roomCode: string;
  roomName: string;
  department: string;
  location: string;
  facility: string;
  status: RoomStatus;
  capacityPerSlot: number;
}

export interface RoomFilters {
  search: string;
  department: string;
  status: string;
}

export interface RoomMetrics {
  totalRooms: number;
  activeRooms: number;
  maintenanceRooms: number;
  linkedDepartments: number;
}
