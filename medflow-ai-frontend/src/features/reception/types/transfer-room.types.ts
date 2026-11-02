export type TransferReasonCode =
  'TECHNICAL_FAILURE' | 'URGENT_CONSULTATION' | 'LOAD_BALANCING' | 'EXTRA_EXPERTISE' | 'OTHER';

export interface TransferReasonOption {
  code: TransferReasonCode;
  label: string;
}

export interface CompatibleRoom {
  id: string;
  label: string;
  location: string;
}

export interface TransferHistoryEntry {
  id: string;
  fromRoom: string;
  toRoom: string;
  reason: string;
  note?: string;
  performedBy: string;
  performedAt: string;
}

export interface TransferQueueVisit {
  patientName: string;
  gender: 'Nam' | 'Nữ';
  age: number;
  patientCode: string;
  visitCode: string;
  currentRoom: string;
  department: string;
  compatibleRooms: CompatibleRoom[];
  history: TransferHistoryEntry[];
}

export interface TransferRoomPayload {
  visitCode: string;
  targetRoomId: string;
  reasonCode: TransferReasonCode;
  note?: string;
}
