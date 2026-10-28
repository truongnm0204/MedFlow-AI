export interface WorkHistoryEntry {
  year: string;
  description: string;
}

export interface Doctor {
  id: string;
  staffId: string;
  name: string;
  email: string;
  degree: string;
  specialty: string;
  department: string;
  license: string;
  room: string;
  phone: string;
  experience: number;
  scope: string;
  biography: string;
  active: boolean;
  portrait?: string;
  history: WorkHistoryEntry[];
}
