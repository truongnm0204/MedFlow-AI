export type RoomType = 'OUTPATIENT' | 'PROCEDURAL';
export type RoomStatus = 'ACTIVE' | 'MAINTENANCE' | 'INACTIVE';
export type RoomCapacity = 1 | 2 | 3 | 4;

export interface UpdateRoomInput {
  name: string;
  type: RoomType;
  department: string;
  location: string;
  capacity: RoomCapacity;
  status: RoomStatus;
}

export interface RoomRecord extends UpdateRoomInput {
  id: string;
  code: string;
}
