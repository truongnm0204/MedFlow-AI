import { create } from 'zustand';

interface BookingState {
  selectedDoctorId: string | null;
  selectedServiceId: string | null;
  selectedServiceName: string | null;
  selectedPatientId: string;
  selectedDateIso: string;
  selectedSlotId: string;
  selectedShiftId: string;
  symptomNote: string;
  selectDoctor: (id: string) => void;
  selectService: (id: string, name?: string) => void;
  selectPatient: (id: string) => void;
  selectDate: (dateIso: string) => void;
  selectSlot: (slotId: string) => void;
  selectShift: (shiftId: string) => void;
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
  symptomNote: '',
  selectDoctor: (id) => set({ selectedDoctorId: id }),
  selectService: (id, name) => set({ selectedServiceId: id, selectedServiceName: name ?? null }),
  selectPatient: (id) => set({ selectedPatientId: id }),
  selectDate: (dateIso) => set({ selectedDateIso: dateIso }),
  selectSlot: (slotId) => set({ selectedSlotId: slotId }),
  selectShift: (shiftId) => set({ selectedShiftId: shiftId }),
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
      symptomNote: '',
    }),
}));
