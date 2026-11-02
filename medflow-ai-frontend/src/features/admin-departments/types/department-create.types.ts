export type DepartmentStatus = 'ACTIVE' | 'INACTIVE';

export interface FacilityOption {
  id: string;
  name: string;
}

export interface CreateDepartmentInput {
  code: string;
  name: string;
  facilityId: string;
  status: DepartmentStatus;
}
