import { create } from 'zustand';

import { INITIAL_DOCTORS } from '../data/doctor-demo-data';
import type { Doctor } from '../types/doctor.types';

interface DoctorDemoState {
  doctors: Doctor[];
  draft: Doctor | null;
  addDoctor: (doctor: Doctor) => void;
  updateDoctor: (doctor: Doctor) => void;
  saveDraft: (doctor: Doctor) => void;
  clearDraft: () => void;
}

/** Chỉ giữ dữ liệu minh họa trong bộ nhớ của tab hiện tại. */
export const useDoctorDemoStore = create<DoctorDemoState>((set) => ({
  doctors: INITIAL_DOCTORS,
  draft: null,
  addDoctor: (doctor) => set((state) => ({ doctors: [doctor, ...state.doctors], draft: null })),
  updateDoctor: (doctor) =>
    set((state) => ({
      doctors: state.doctors.map((item) => (item.id === doctor.id ? doctor : item)),
    })),
  saveDraft: (draft) => set({ draft }),
  clearDraft: () => set({ draft: null }),
}));
