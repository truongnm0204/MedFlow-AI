export interface CompatibleExaminationRoomItemDto {
  roomId: number;
  roomCode: string;
  roomName: string;
  hospitalId: number | null;
  departmentId: number | null;
  departmentCode: string | null;
  departmentName: string | null;
  roomType: string;
  location: string | null;
  capacity: number;
  activeVisitCount: number;
  availableCapacity: number;
  status: string;
  isCurrentRoom: boolean;
  isCompatible: boolean;
  isAvailable: boolean;
  unavailableReason: string | null;
}

export interface VisitRoomTransferHistoryItemDto {
  auditLogId: number;
  fromRoomId: number | null;
  fromRoomCode: string | null;
  fromRoomName: string | null;
  toRoomId: number;
  toRoomCode: string;
  toRoomName: string;
  previousDoctorId: number | null;
  assignedDoctorId: number | null;
  reason: string;
  notes: string | null;
  transferredByUserId: number | null;
  transferredByStaffCode: string | null;
  transferredByName: string | null;
  transferredAtUtc: string;
}

export interface VisitCompatibleRoomsResponseDto {
  visitId: number;
  visitCode: string;
  patientId: number | null;
  patientCode: string | null;
  patientName: string | null;
  visitStatus: string;
  currentRoomId: number | null;
  currentRoomCode: string | null;
  currentRoomName: string | null;
  departmentId: number | null;
  departmentCode: string | null;
  departmentName: string | null;
  assignedDoctorId: number | null;
  assignedDoctorCode: string | null;
  assignedDoctorName: string | null;
  queueNumber: number | null;
  queueStatus: string | null;
  compatibleRooms: CompatibleExaminationRoomItemDto[];
  transferHistory: VisitRoomTransferHistoryItemDto[];
}

export interface TransferVisitRoomRequestPayload {
  targetRoomId?: number | null;
  targetRoomCode?: string | null;
  reason: string;
  notes?: string | null;
  targetDoctorId?: number | null;
}

export interface VisitRoomTransferResponseDto {
  visitId: number;
  visitCode: string;
  patientId: number | null;
  patientCode: string | null;
  patientName: string | null;
  visitStatus: string;
  previousRoomId: number;
  previousRoomCode: string;
  previousRoomName: string;
  currentRoomId: number;
  currentRoomCode: string;
  currentRoomName: string;
  departmentId: number | null;
  departmentCode: string | null;
  departmentName: string | null;
  previousDoctorId: number | null;
  assignedDoctorId: number | null;
  assignedDoctorCode: string | null;
  assignedDoctorName: string | null;
  queueNumber: number | null;
  queueStatus: string | null;
  transferReason: string;
  notes: string | null;
  transferredByUserId: number | null;
  transferredByStaffCode: string | null;
  transferredByName: string | null;
  transferredAtUtc: string;
  transferHistory: VisitRoomTransferHistoryItemDto[];
}

export interface TransferVisitRoomTarget {
  visitIdOrCode: string;
  patientCode: string;
  patientName: string;
  currentRoomCode: string;
  ticketNo?: string;
  departmentName?: string;
  assignedDoctorName?: string;
}
