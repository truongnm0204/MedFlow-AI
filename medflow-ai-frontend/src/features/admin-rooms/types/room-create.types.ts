export type RoomType = 'OUTPATIENT' | 'PROCEDURAL';
export type RoomStatus = 'ACTIVE' | 'MAINTENANCE' | 'INACTIVE';

export interface CreateRoomInput {
  code: string;
  name: string;
  type: RoomType;
  department: string;
  location: string;
  capacity: number;
  status: RoomStatus;
}
