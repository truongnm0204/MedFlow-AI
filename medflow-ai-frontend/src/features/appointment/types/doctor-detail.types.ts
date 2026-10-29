export interface EducationItem {
  id: string;
  title: string;
  institution: string;
  badge?: string;
  type: 'degree' | 'specialist' | 'certificate';
}

export interface WeeklyScheduleDay {
  dayOfWeek: string;
  hasAvailableSlot?: boolean;
  morningTime?: string;
  afternoonTime?: string;
  note?: string;
  statusTag?: string;
  isActive?: boolean;
}

export interface DoctorDetailProfile {
  id: string;
  name: string;
  title: string;
  roleTitle: string;
  specialty: string;
  room: string;
  statusText: string;
  price: number;
  avatarUrl: string;
  education: EducationItem[];
  biography: string;
  hashtags: string[];
  weeklySchedule: WeeklyScheduleDay[];
}
