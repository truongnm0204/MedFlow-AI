import { create } from 'zustand';

import type { BookingMode } from '../types/booking-entry.types';

interface BookingState {
  selectedDoctorId: string | null;
  selectedServiceId: string | null;
  selectedServiceName: string | null;
  selectedPatientId: string;
  selectedDateIso: string;
  selectedSlotId: string;
  selectedShiftId: string;
  selectedHospitalId: string | null;
  bookingMode: BookingMode | null;
  symptomNote: string;
  selectDoctor: (id: string) => void;
  selectService: (id: string, name?: string) => void;
  selectPatient: (id: string) => void;
  selectDate: (dateIso: string) => void;
  selectSlot: (slotId: string) => void;
  selectShift: (shiftId: string) => void;
  selectHospital: (id: string) => void;
  selectBookingMode: (mode: BookingMode) => void;
  setSymptomNote: (note: string) => void;
  reset: () => void;
}

export const useBookingStore = create<BookingState>((set) => ({
  selectedDoctorId: null,
  selectedServiceId: null,
  selectedServiceName: null,
  selectedPatientId: 'pat-self',
  selectedDateIso: '2026-10-13',
  selectedSlotId: 'slot-0900',
  selectedShiftId: 'shift-morning',
  selectedHospitalId: null,
  bookingMode: null,
  symptomNote: '',
  selectDoctor: (id) =>
    set({ selectedDoctorId: id, selectedServiceId: null, selectedServiceName: null }),
  selectService: (id, name) =>
    set({ selectedServiceId: id, selectedServiceName: name ?? null, selectedDoctorId: null }),
  selectPatient: (id) => set({ selectedPatientId: id }),
  selectDate: (dateIso) => set({ selectedDateIso: dateIso }),
  selectSlot: (slotId) => set({ selectedSlotId: slotId }),
  selectShift: (shiftId) => set({ selectedShiftId: shiftId }),
  selectHospital: (id) => set({ selectedHospitalId: id }),
  selectBookingMode: (mode) => set({ bookingMode: mode }),
  setSymptomNote: (note) => set({ symptomNote: note }),
  reset: () =>
    set({
      selectedDoctorId: null,
      selectedServiceId: null,
      selectedServiceName: null,
      selectedPatientId: 'pat-self',
      selectedDateIso: '2026-10-13',
      selectedSlotId: 'slot-0900',
      selectedShiftId: 'shift-morning',
      selectedHospitalId: null,
      bookingMode: null,
      symptomNote: '',
    }),
}));
