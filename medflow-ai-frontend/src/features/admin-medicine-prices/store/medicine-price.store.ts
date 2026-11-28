import { create } from 'zustand';

import type { MedicinePriceItem } from '../types/medicine-price.types';

interface MedicinePriceState {
  items: MedicinePriceItem[];
  updatePriceMedicine: MedicinePriceItem | null;
  priceHistoryMedicine: MedicinePriceItem | null;
  setItems: (items: MedicinePriceItem[]) => void;
  setUpdatePriceMedicine: (medicine: MedicinePriceItem | null) => void;
  setPriceHistoryMedicine: (medicine: MedicinePriceItem | null) => void;
  reset: () => void;
}

export const useMedicinePriceStore = create<MedicinePriceState>((set) => ({
  items: [],
  updatePriceMedicine: null,
  priceHistoryMedicine: null,
  setItems: (items) => set({ items }),
  setUpdatePriceMedicine: (medicine) => set({ updatePriceMedicine: medicine }),
  setPriceHistoryMedicine: (medicine) => set({ priceHistoryMedicine: medicine }),
  reset: () => set({ items: [], updatePriceMedicine: null, priceHistoryMedicine: null }),
}));
