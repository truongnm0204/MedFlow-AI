import { create } from 'zustand';

import { INITIAL_OPERATIONAL_REPORTS } from '../data/operational-reports-data';
import type { OperationalReportItem } from '../types/operational-report.types';

interface OperationalReportState {
  reports: OperationalReportItem[];
  searchQuery: string;
  periodFilter: string;
  facilityFilter: string;
  categoryFilter: string;
  page: number;
  pageSize: number;
  selectedIds: string[];

  setSearchQuery: (query: string) => void;
  setPeriodFilter: (period: string) => void;
  setFacilityFilter: (facility: string) => void;
  setCategoryFilter: (category: string) => void;
  resetFilters: () => void;
  setPage: (page: number) => void;
  setPageSize: (pageSize: number) => void;
  toggleSelection: (id: string) => void;
  toggleAll: (visibleIds: string[]) => void;
  clearSelection: () => void;
  refreshReport: (id: string) => void;
}

export const useOperationalReportStore = create<OperationalReportState>((set) => ({
  reports: INITIAL_OPERATIONAL_REPORTS,
  searchQuery: '',
  periodFilter: '22/10/2026',
  facilityFilter: '',
  categoryFilter: '',
  page: 1,
  pageSize: 20,
  selectedIds: [],

  setSearchQuery: (searchQuery) => set({ searchQuery, page: 1 }),
  setPeriodFilter: (periodFilter) => set({ periodFilter, page: 1 }),
  setFacilityFilter: (facilityFilter) => set({ facilityFilter, page: 1 }),
  setCategoryFilter: (categoryFilter) => set({ categoryFilter, page: 1 }),
  resetFilters: () =>
    set({
      searchQuery: '',
      periodFilter: '22/10/2026',
      facilityFilter: '',
      categoryFilter: '',
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
  refreshReport: (id) =>
    set((state) => ({
      reports: state.reports.map((report) =>
        report.id === id
          ? {
              ...report,
              aggregationStatus: 'AGGREGATED',
              status: 'COMPLETED',
            }
          : report,
      ),
    })),
}));
