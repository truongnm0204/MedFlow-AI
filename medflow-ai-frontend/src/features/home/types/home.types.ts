import type { ReactNode } from 'react';

export interface Specialty {
  id: string;
  name: string;
  desc: string;
  doctors: number;
  price: string;
  tag: string;
  icon: ReactNode;
}

export interface Doctor {
  id: string;
  name: string;
  role: string;
  degree: string;
  hospital: string;
  earliestSlot: string;
  fee: string;
  rating: string;
  avatar: string;
}

export interface JourneyStep {
  step: string;
  title: string;
  desc: string;
}
