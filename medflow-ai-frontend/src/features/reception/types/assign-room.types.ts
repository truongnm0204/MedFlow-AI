export type RoomCompatibilityStatus = 'READY' | 'BUSY' | 'OVERLOADED';

export interface AvailableRoomItem {
  id: string;
  code: string;
  name: string;
  location: string;
  status: RoomCompatibilityStatus;
  /** Sức chứa (người/slot); undefined nếu không hiển thị */
  capacityPerSlot?: number;
  waitingCount: number;
  /** Mức tương thích 0-100 */
  compatibilityPercent: number;
}

export interface AssignRoomPayload {
  roomId: string;
  serviceName: string;
}
