export type VisitPriority = 'NORMAL' | 'URGENT' | 'ELDERLY' | 'PREGNANT' | 'RETURN_RESULT';

export interface CompatibleExaminationRoomItemDto {
  id: number;
  roomCode: string;
  name: string;
  roomType: string;
  location: string | null;
  hospitalId: number | null;
  departmentId: number | null;
  departmentCode: string | null;
  departmentName: string | null;
  capacity: number | null;
  currentOccupancy: number;
  availableCapacity: number;
  status: string;
  isCompatible: boolean;
  isAvailable: boolean;
  unavailableReason: string | null;
}

export interface CompatibleExaminationRoomListResponseDto {
  visitId: number;
  visitCode: string;
  visitStatus: string;
  hospitalId: number | null;
  assignedDoctorId: number | null;
  assignedDoctorCode: string | null;
  assignedDoctorName: string | null;
  currentExaminationRoomId: number | null;
  currentExaminationRoomCode: string | null;
  requiredDepartmentId: number | null;
  requiredDepartmentName: string | null;
  rooms: CompatibleExaminationRoomItemDto[];
}

export interface AssignExaminationRoomPayload {
  examinationRoomId?: number | null;
  roomId?: number | null;
  roomCode?: string | null;
  priority?: VisitPriority | null;
  reason?: string | null;
  note?: string | null;
}

export interface AssignExaminationRoomResponseDto {
  visitId: number;
  visitCode: string;
  patientId: number | null;
  patientCode: string | null;
  patientName: string | null;
  appointmentId: number | null;
  appointmentCode: string | null;
  assignedDoctorId: number | null;
  assignedDoctorCode: string | null;
  assignedDoctorName: string | null;
  examinationRoomId: number;
  roomCode: string;
  roomName: string;
  roomType: string;
  location: string | null;
  departmentId: number | null;
  departmentCode: string | null;
  departmentName: string | null;
  hospitalId: number | null;
  visitStatus: string;
  previousRoomId: number | null;
  previousRoomCode: string | null;
  queueEntryId: number | null;
  queueNumber: number | null;
  queueCode: string | null;
  queueStatus: string | null;
  priority: VisitPriority | null;
  servicePointId: number | null;
  servicePointName: string | null;
  roomCapacity: number | null;
  currentRoomOccupancy: number;
  checkedInByStaffId: number | null;
  checkedInAt: string | null;
  assignedAtUtc: string;
}

export interface AssignRoomVisitTarget {
  visitIdOrCode: string;
  patientCode: string;
  patientName: string;
  currentRoomCode?: string;
  priority?: VisitPriority;
  assignedDoctorName?: string;
  departmentName?: string;
}
