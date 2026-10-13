import { create } from 'zustand';

interface BookingState {
  selectedDoctorId: string | null;
  selectDoctor: (id: string) => void;
  reset: () => void;
}

export const useBookingStore = create<BookingState>((set) => ({
  selectedDoctorId: null,
  selectDoctor: (id) => set({ selectedDoctorId: id }),
  reset: () => set({ selectedDoctorId: null }),
}));
