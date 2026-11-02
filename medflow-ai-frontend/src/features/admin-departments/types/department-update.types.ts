export type DepartmentStatus = 'ACTIVE' | 'INACTIVE';

export interface Department {
  code: string;
  name: string;
  facility: string;
  status: DepartmentStatus;
}

export interface UpdateDepartmentInput {
  name: string;
  facility: string;
  status: DepartmentStatus;
}
