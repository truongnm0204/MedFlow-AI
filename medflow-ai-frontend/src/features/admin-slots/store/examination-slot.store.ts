import { create } from 'zustand';

import { registerAdminDraftCleanup } from '@/shared/lib/admin-draft-storage';

import {
  batchCloseAdminExaminationSlotsApi,
  closeAdminExaminationSlotApi,
  reopenAdminExaminationSlotApi,
} from '../api/examination-slot.api';
import type {
  CloseSlotPayload,
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
  updateSlot: (id: string, updated: Partial<ExaminationSlot>) => void;
  saveDraft: (draft: Partial<ExaminationSlotFormValues>) => void;
  clearDraft: () => void;
  closeSlot: (
    id: string,
    payload: CloseSlotPayload,
  ) => Promise<{ success: boolean; message: string }>;
  reopenSlot: (id: string) => Promise<{ success: boolean; message: string }>;
  batchCloseSlots: (
    ids: string[],
    payload: Omit<CloseSlotPayload, 'slotId'>,
  ) => Promise<{ success: boolean; count: number }>;
}

export const useExaminationSlotStore = create<ExaminationSlotStore>((set) => ({
  slots: [],
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
  updateSlot: (id, updated) =>
    set((state) => ({
      slots: state.slots.map((item) => {
        if (item.id === id || item.code === id) {
          const next = { ...item, ...updated };
          if (updated.date && !updated.dateDisplay) {
            const parts = updated.date.split('-');
            if (parts.length === 3) {
              next.dateDisplay = `${parts[2]}/${parts[1]}`;
            }
          }
          return next;
        }
        return item;
      }),
    })),
  closeSlot: async (id, payload) => {
    const res = await closeAdminExaminationSlotApi(id, payload);
    const now = new Date().toISOString();
    set((state) => ({
      slots: state.slots.map((item) => {
        if (item.id === id || item.code === id) {
          return {
            ...item,
            status: 'CLOSED',
            closedAt: now,
            closeReason: payload.reasonText || payload.reasonCode,
            closeReasonCode: payload.reasonCode,
            affectedBookingsAction: payload.affectedBookingsAction,
          };
        }
        return item;
      }),
    }));
    return res;
  },

  reopenSlot: async (id) => {
    const res = await reopenAdminExaminationSlotApi(id);
    set((state) => ({
      slots: state.slots.map((item) => {
        if (item.id === id || item.code === id) {
          const nextStatus: SlotStatus =
            item.capacityBooked >= item.capacityMax && item.capacityMax > 0 ? 'FULL' : 'OPEN';
          return {
            ...item,
            status: nextStatus,
            closedAt: undefined,
            closeReason: undefined,
            closeReasonCode: undefined,
            affectedBookingsAction: undefined,
          };
        }
        return item;
      }),
    }));
    return res;
  },

  batchCloseSlots: async (ids, payload) => {
    const res = await batchCloseAdminExaminationSlotsApi(ids, payload);
    const now = new Date().toISOString();
    set((state) => ({
      slots: state.slots.map((item) => {
        if (ids.includes(item.id) || ids.includes(item.code)) {
          return {
            ...item,
            status: 'CLOSED',
            closedAt: now,
            closeReason: payload.reasonText || payload.reasonCode,
            closeReasonCode: payload.reasonCode,
            affectedBookingsAction: payload.affectedBookingsAction,
          };
        }
        return item;
      }),
      selectedIds: state.selectedIds.filter((id) => !ids.includes(id)),
    }));
    return res;
  },

  saveDraft: (draft) => set({ draft }),

  clearDraft: () => set({ draft: null }),
}));

registerAdminDraftCleanup(() => {
  useExaminationSlotStore.setState({ draft: null });
});
