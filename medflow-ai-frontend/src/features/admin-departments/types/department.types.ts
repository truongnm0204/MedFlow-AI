export type DepartmentStatus = 'ACTIVE' | 'INACTIVE';

export interface DepartmentItem {
  id: string;
  code: string;
  name: string;
  facility: string;
  status: DepartmentStatus;
}

export interface DepartmentFilters {
  search: string;
  facility: string;
  status: '' | DepartmentStatus;
}

export interface DepartmentMetrics {
  total: number;
  active: number;
  inactive: number;
  facilities: number;
}
