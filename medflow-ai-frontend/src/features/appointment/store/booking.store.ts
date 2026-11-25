import { create } from 'zustand';

import { getHospitalTodayIso } from '@/shared/lib/hospital-date';

import type { BookingMode } from '../types/booking-entry.types';

interface SelectServiceOptions {
  preserveDoctor?: boolean;
}

interface RebookSelectionPayload {
  doctorId?: string | null;
  serviceId?: string | null;
  serviceName?: string | null;
  patientId?: string | null;
}

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
  selectService: (id: string, name?: string, options?: SelectServiceOptions) => void;
  prepareRebook: (payload: RebookSelectionPayload) => void;
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
  selectedDateIso: getHospitalTodayIso(),
  selectedSlotId: 'slot-0900',
  selectedShiftId: 'shift-morning',
  selectedHospitalId: null,
  bookingMode: null,
  symptomNote: '',
  selectDoctor: (id) =>
    set({ selectedDoctorId: id || null, selectedServiceId: null, selectedServiceName: null }),
  selectService: (id, name, options) =>
    set((state) => ({
      selectedServiceId: id || null,
      selectedServiceName: name ?? null,
      selectedDoctorId: options?.preserveDoctor ? state.selectedDoctorId : null,
    })),
  prepareRebook: ({ doctorId, serviceId, serviceName, patientId }) =>
    set((state) => ({
      selectedDoctorId: doctorId?.trim() ? doctorId.trim() : null,
      selectedServiceId: serviceId?.trim() ? serviceId.trim() : null,
      selectedServiceName: serviceName?.trim() ? serviceName.trim() : null,
      selectedPatientId: patientId?.trim() ? patientId.trim() : state.selectedPatientId,
      bookingMode: doctorId?.trim() ? 'DOCTOR' : 'SERVICE',
    })),
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
      selectedDateIso: getHospitalTodayIso(),
      selectedSlotId: 'slot-0900',
      selectedShiftId: 'shift-morning',
      selectedHospitalId: null,
      bookingMode: null,
      symptomNote: '',
    }),
}));
