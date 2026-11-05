export type DeactivateDepartmentReason =
  'RESTRUCTURING' | 'MAINTENANCE' | 'OPERATION_SUSPENDED' | 'OTHER';

export interface DeactivateDepartmentPayload {
  departmentId: string;
  reason: DeactivateDepartmentReason;
  note?: string;
}

export interface DepartmentDependencySummary {
  activeRoomsCount: number;
  activeDoctorsCount: number;
  activeServicesCount: number;
  pendingAppointmentsCount: number;
  canDeactivate: boolean;
  blockingReason?: string;
}
