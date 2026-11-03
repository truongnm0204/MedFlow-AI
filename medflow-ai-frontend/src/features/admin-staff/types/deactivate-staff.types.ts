export type DeactivateStaffReason = 'RESIGNED' | 'TRANSFERRED' | 'SUSPENDED' | 'OTHER';

export type StaffAccountStatus = 'ACTIVE' | 'DISABLED';

export interface StaffTargetSummary {
  id: string;
  staffCode: string;
  fullName: string;
  role: string;
  department: string;
  email: string;
  /** Number of exams/appointments still assigned to this staff member. */
  pendingAssignments: number;
}

export interface DeactivateStaffPayload {
  staffId: string;
  reason: DeactivateStaffReason;
  note?: string;
}
