export type PatientProfileBadgeVariant = 'primary' | 'pediatric' | 'geriatric';

export interface BookingPatientProfile {
  id: string;
  fullName: string;
  relationshipLabel: string;
  badgeText: string;
  badgeVariant: PatientProfileBadgeVariant;
  gender: 'Nam' | 'Nữ';
  age: number;
  metaPrimary: string;
  metaSecondary: string;
  patientCode?: string;
}

export type DateScheduleStatus = 'FULL' | 'AVAILABLE' | 'FEW_SLOTS' | 'OFF';

export type ExaminationShift = 'MORNING' | 'AFTERNOON';

export interface BookingTimeSlot {
  id: string;
  timeRange: string;
  shift: ExaminationShift;
  shiftLabel: string;
  status: TimeSlotStatus;
  remainingSlots: number;
  badgeText: string;
}

export type TimeSlotStatus = 'FULL' | 'AVAILABLE' | 'LAST_SLOT';

export interface DoctorDayShiftSchedule {
  hasMorningShift: boolean;
  hasAfternoonShift: boolean;
  shiftSummaryLabel: string;
  offShiftNote?: string;
  morningSlots: BookingTimeSlot[];
  afternoonSlots: BookingTimeSlot[];
}

export interface BookingDateOption {
  dateIso: string;
  dayTopLabel: string;
  dayNumber: string;
  subDateLabel: string;
  fullDateDisplay: string;
  status: DateScheduleStatus;
  remainingSlots: number;
  badgeText: string;
  isToday?: boolean;
  doctorShiftSummary?: string;
}

export interface BookingDoctorServiceInfo {
  doctorId: string;
  doctorName: string;
  roleTag: string;
  specialty: string;
  room: string;
  shortRoom: string;
  serviceId: string;
  serviceCode: string;
  serviceName: string;
  price: number;
  avatarUrl: string;
  availabilityLabel: string;
  dutyNote: string;
}

export interface ServiceShiftOption {
  id: string;
  shift: ExaminationShift;
  title: string;
  timeRange: string;
  checkInWindow: string;
  remainingCapacity: number;
  badgeText: string;
  status: 'AVAILABLE' | 'FEW_SLOTS' | 'FULL';
  description: string;
}

export interface BookingServiceOnlyInfo {
  serviceId: string;
  serviceCode: string;
  serviceName: string;
  departmentName: string;
  location: string;
  durationMinutes: number;
  durationDescription: string;
  price: number;
  onlineBadgeLabel: string;
}

export interface AvailableSlotsPayload {
  doctorService: BookingDoctorServiceInfo;
  monthLabel: string;
  profiles: BookingPatientProfile[];
  dates: BookingDateOption[];
  morningSlots: BookingTimeSlot[];
  afternoonSlots: BookingTimeSlot[];
  activeDayShift: DoctorDayShiftSchedule;
}

export interface ServiceAvailableShiftsPayload {
  serviceInfo: BookingServiceOnlyInfo;
  monthLabel: string;
  profiles: BookingPatientProfile[];
  dates: BookingDateOption[];
  shifts: ServiceShiftOption[];
}
