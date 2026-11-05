export interface AppointmentPolicyDto {
  bookingWindowDays: number;
  bookingLeadHours: number;
  maxActiveBookings: number;
  rescheduleMinimumHours: number;
  maxReschedules: number;
  cancelMinimumHours: number;
  noShowGraceMinutes: number;
  noShowMaxCount: number;
  noShowLookbackDays: number;
  noShowBlockDays: number;
  lastUpdatedAt: string | null;
}

export type UpdateAppointmentPolicyRequest = Omit<AppointmentPolicyDto, 'lastUpdatedAt'>;
