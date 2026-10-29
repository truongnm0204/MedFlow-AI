import { create } from 'zustand';

import { INITIAL_SERVICES } from '../data/examination-services-data';
import type { ColumnKey, ExaminationService } from '../types/examination-service.types';

const DEFAULT_VISIBLE_COLUMNS: Record<ColumnKey, boolean> = {
  code: true,
  name: true,
  category: true,
  department: true,
  price: true,
  booking: true,
  status: true,
  actions: true,
};

interface ExaminationServiceState {
  services: ExaminationService[];
  searchQuery: string;
  categoryFilter: string;
  departmentFilter: string;
  bookingFilter: string;
  statusFilter: string;
  page: number;
  pageSize: number;
  selectedIds: string[];
  visibleColumns: Record<ColumnKey, boolean>;
  priceHistoryService: ExaminationService | null;
  isColumnModalOpen: boolean;

  setSearchQuery: (query: string) => void;
  setCategoryFilter: (category: string) => void;
  setDepartmentFilter: (department: string) => void;
  setBookingFilter: (booking: string) => void;
  setStatusFilter: (status: string) => void;
  resetFilters: () => void;
  setPage: (page: number) => void;
  setPageSize: (pageSize: number) => void;
  toggleSelection: (id: string) => void;
  toggleAll: (visibleIds: string[]) => void;
  clearSelection: () => void;
  toggleStatus: (id: string) => void;
  batchToggleStatus: (newStatus: 'ACTIVE' | 'SUSPENDED') => void;
  toggleColumn: (key: ColumnKey) => void;
  setPriceHistoryService: (service: ExaminationService | null) => void;
  setIsColumnModalOpen: (open: boolean) => void;
  draft: Partial<ExaminationService> | null;
  addService: (service: ExaminationService) => void;
  updateService: (id: string, updated: Partial<ExaminationService>) => void;
  saveDraft: (draft: Partial<ExaminationService>) => void;
  clearDraft: () => void;
}

export const useExaminationServiceStore = create<ExaminationServiceState>((set) => ({
  services: INITIAL_SERVICES,
  searchQuery: '',
  categoryFilter: '',
  departmentFilter: '',
  bookingFilter: '',
  statusFilter: '',
  page: 1,
  pageSize: 10,
  selectedIds: [],
  visibleColumns: DEFAULT_VISIBLE_COLUMNS,
  priceHistoryService: null,
  isColumnModalOpen: false,

  setSearchQuery: (query) => set({ searchQuery: query, page: 1 }),
  setCategoryFilter: (category) => set({ categoryFilter: category, page: 1 }),
  setDepartmentFilter: (department) => set({ departmentFilter: department, page: 1 }),
  setBookingFilter: (booking) => set({ bookingFilter: booking, page: 1 }),
  setStatusFilter: (status) => set({ statusFilter: status, page: 1 }),
  resetFilters: () =>
    set({
      searchQuery: '',
      categoryFilter: '',
      departmentFilter: '',
      bookingFilter: '',
      statusFilter: '',
      page: 1,
    }),
  setPage: (page) => set({ page }),
  setPageSize: (pageSize) => set({ pageSize, page: 1 }),

  toggleSelection: (id) =>
    set((state) => ({
      selectedIds: state.selectedIds.includes(id)
        ? state.selectedIds.filter((item) => item !== id)
        : [...state.selectedIds, id],
    })),

  toggleAll: (visibleIds) =>
    set((state) => {
      const allSelected =
        visibleIds.length > 0 && visibleIds.every((id) => state.selectedIds.includes(id));
      return {
        selectedIds: allSelected
          ? state.selectedIds.filter((id) => !visibleIds.includes(id))
          : [...new Set([...state.selectedIds, ...visibleIds])],
      };
    }),

  clearSelection: () => set({ selectedIds: [] }),

  toggleStatus: (id) =>
    set((state) => ({
      services: state.services.map((item) =>
        item.id === id
          ? { ...item, status: item.status === 'ACTIVE' ? 'SUSPENDED' : 'ACTIVE' }
          : item,
      ),
    })),

  batchToggleStatus: (newStatus) =>
    set((state) => ({
      services: state.services.map((item) =>
        state.selectedIds.includes(item.id) ? { ...item, status: newStatus } : item,
      ),
      selectedIds: [],
    })),

  toggleColumn: (key) =>
    set((state) => ({
      visibleColumns: {
        ...state.visibleColumns,
        [key]: !state.visibleColumns[key],
      },
    })),

  setPriceHistoryService: (service) => set({ priceHistoryService: service }),
  setIsColumnModalOpen: (open) => set({ isColumnModalOpen: open }),

  draft: null,
  addService: (service) =>
    set((state) => ({
      services: [service, ...state.services],
      draft: null,
    })),
  updateService: (id, updated) =>
    set((state) => ({
      services: state.services.map((item) =>
        item.id === id || item.code === id ? { ...item, ...updated } : item,
      ),
    })),
  saveDraft: (draft) => set({ draft }),
  clearDraft: () => set({ draft: null }),
}));
