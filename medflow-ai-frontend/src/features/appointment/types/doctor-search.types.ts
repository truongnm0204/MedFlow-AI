export type ScheduleBadgeStatus =
  'AVAILABLE' | 'FEW_SLOTS' | 'ADVANCE_24H' | 'ALMOST_FULL' | 'WEEKEND';

export interface DoctorSearchResult {
  id: string;
  name: string;
  title: string;
  roleTag: string;
  specialty: string;
  specialtyId: string;
  yearsOfExperience: number;
  room: string;
  nearestSchedule: string;
  scheduleStatus: ScheduleBadgeStatus;
  scheduleStatusText: string;
  price: number;
  avatarUrl: string;
  hasScheduleToday: boolean;
  hasScheduleThisWeek: boolean;
}

export interface DoctorFilterParams {
  keyword?: string;
  specialty?: string;
  degree?: string;
  hasScheduleToday?: boolean;
  hasScheduleThisWeek?: boolean;
  page?: number;
  pageSize?: number;
}

export interface DoctorSearchResponse {
  items: DoctorSearchResult[];
  total: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface SpecialtyTab {
  id: string;
  name: string;
}
