export type IdentityClaimStatus =
  'PENDING' | 'APPROVED' | 'NEEDS_RECONCILIATION' | 'REJECTED' | 'EXPIRED' | 'CANCELLED';

export interface IdentityClaim {
  claimId: number;
  status: IdentityClaimStatus;
  createdAt: string;
  expiresAt: string;
  decidedAt: string | null;
  decisionNote: string | null;
}

export interface StaffIdentityClaim {
  claimId: number;
  requesterUserId: number;
  requestedPatientCode: string;
  targetPatientId: number | null;
  currentPatientId: number | null;
  hospitalId: number;
  status: IdentityClaimStatus;
  createdAt: string;
  expiresAt: string;
}

export interface IdentityHospital {
  id: number;
  name: string;
  isActive: boolean;
}

export interface CreateIdentityClaimPayload {
  patientCode: string;
  hospitalId: number;
  otp: string;
}
