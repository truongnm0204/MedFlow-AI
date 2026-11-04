import { create } from 'zustand';

import { DEPARTMENT_MOCK_DATA } from '../data/department-mock-data';
import type { DeactivateDepartmentPayload } from '../types/department-deactivate.types';
import type { DepartmentItem } from '../types/department.types';

interface DepartmentState {
  departments: DepartmentItem[];
  setDepartments: (departments: DepartmentItem[]) => void;
  updateDepartment: (id: string, updated: Partial<DepartmentItem>) => void;
  deactivateDepartment: (id: string, payload?: Partial<DeactivateDepartmentPayload>) => void;
  activateDepartment: (id: string) => void;
  resetToMock: () => void;
}

export const useDepartmentStore = create<DepartmentState>((set) => ({
  departments: DEPARTMENT_MOCK_DATA,
  setDepartments: (departments) => set({ departments }),
  updateDepartment: (id, updated) =>
    set((state) => ({
      departments: state.departments.map((dept) =>
        dept.id === id || dept.code === id ? { ...dept, ...updated } : dept,
      ),
    })),
  deactivateDepartment: (id, payload) =>
    set((state) => ({
      departments: state.departments.map((dept) =>
        dept.id === id || dept.code === id
          ? {
              ...dept,
              status: 'INACTIVE',
              deactivateReason: payload?.reason,
              hasActiveDependencies: false,
            }
          : dept,
      ),
    })),
  activateDepartment: (id) =>
    set((state) => ({
      departments: state.departments.map((dept) =>
        dept.id === id || dept.code === id
          ? {
              ...dept,
              status: 'ACTIVE',
            }
          : dept,
      ),
    })),
  resetToMock: () => set({ departments: DEPARTMENT_MOCK_DATA }),
}));
