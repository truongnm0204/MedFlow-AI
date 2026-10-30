import { create } from 'zustand';

import { INITIAL_EXAMINATION_SLOTS } from '../data/examination-slots-mock';
import type {
  ExaminationSlot,
  ExaminationSlotFormValues,
  SlotStatus,
} from '../types/examination-slot.types';

interface ExaminationSlotStore {
  slots: ExaminationSlot[];
  searchQuery: string;
  dateFilter: string;
  specialtyFilter: string;
  statusFilter: string;
  page: number;
  pageSize: number;
  selectedIds: string[];
  draft: Partial<ExaminationSlotFormValues> | null;

  // Actions
  setSearchQuery: (query: string) => void;
  setDateFilter: (date: string) => void;
  setSpecialtyFilter: (specialty: string) => void;
  setStatusFilter: (status: string) => void;
  setPage: (page: number) => void;
  setPageSize: (pageSize: number) => void;
  toggleSelection: (id: string) => void;
  toggleAll: (visibleIds: string[]) => void;
  clearSelection: () => void;
  batchUpdateStatus: (status: SlotStatus) => void;
  toggleSlotStatus: (id: string) => void;
  resetFilters: () => void;
  addSlot: (slot: ExaminationSlot) => void;
  saveDraft: (draft: Partial<ExaminationSlotFormValues>) => void;
  clearDraft: () => void;
}

export const useExaminationSlotStore = create<ExaminationSlotStore>((set) => ({
  slots: INITIAL_EXAMINATION_SLOTS,
  searchQuery: '',
  dateFilter: '',
  specialtyFilter: '',
  statusFilter: '',
  page: 1,
  pageSize: 20,
  selectedIds: [],
  draft: null,

  setSearchQuery: (searchQuery) => set({ searchQuery, page: 1 }),
  setDateFilter: (dateFilter) => set({ dateFilter, page: 1 }),
  setSpecialtyFilter: (specialtyFilter) => set({ specialtyFilter, page: 1 }),
  setStatusFilter: (statusFilter) => set({ statusFilter, page: 1 }),
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

  batchUpdateStatus: (status) =>
    set((state) => ({
      slots: state.slots.map((slot) =>
        state.selectedIds.includes(slot.id) ? { ...slot, status } : slot,
      ),
      selectedIds: [],
    })),

  toggleSlotStatus: (id) =>
    set((state) => ({
      slots: state.slots.map((slot) => {
        if (slot.id !== id) return slot;
        const nextStatus: SlotStatus = slot.status === 'OPEN' ? 'CLOSED' : 'OPEN';
        return { ...slot, status: nextStatus };
      }),
    })),

  resetFilters: () =>
    set({
      searchQuery: '',
      dateFilter: '',
      specialtyFilter: '',
      statusFilter: '',
      page: 1,
    }),

  addSlot: (slot) =>
    set((state) => ({
      slots: [slot, ...state.slots],
    })),

  saveDraft: (draft) => set({ draft }),

  clearDraft: () => set({ draft: null }),
}));
