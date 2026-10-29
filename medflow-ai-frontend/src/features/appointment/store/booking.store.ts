import { create } from 'zustand';

interface BookingState {
  selectedDoctorId: string | null;
  selectedServiceId: string | null;
  selectedServiceName: string | null;
  selectDoctor: (id: string) => void;
  selectService: (id: string, name?: string) => void;
  reset: () => void;
}

export const useBookingStore = create<BookingState>((set) => ({
  selectedDoctorId: null,
  selectedServiceId: null,
  selectedServiceName: null,
  selectDoctor: (id) => set({ selectedDoctorId: id }),
  selectService: (id, name) => set({ selectedServiceId: id, selectedServiceName: name ?? null }),
  reset: () =>
    set({
      selectedDoctorId: null,
      selectedServiceId: null,
      selectedServiceName: null,
    }),
}));
