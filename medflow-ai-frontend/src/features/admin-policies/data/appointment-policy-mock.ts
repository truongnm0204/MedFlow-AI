import type {
  AppointmentPolicyDto,
  UpdateAppointmentPolicyRequest,
} from '../types/appointment-policy.types';

export const DEFAULT_APPOINTMENT_POLICY_VALUES: UpdateAppointmentPolicyRequest = {
  bookingWindowDays: 30,
  bookingLeadHours: 2,
  maxActiveBookings: 3,
  rescheduleMinimumHours: 12,
  maxReschedules: 2,
  cancelMinimumHours: 6,
  noShowGraceMinutes: 15,
  noShowMaxCount: 3,
  noShowLookbackDays: 90,
  noShowBlockDays: 30,
};

export const INITIAL_MOCK_APPOINTMENT_POLICY: AppointmentPolicyDto = {
  ...DEFAULT_APPOINTMENT_POLICY_VALUES,
  lastUpdatedAt: '2026-10-01T00:00:00Z',
};

let currentMockPolicy: AppointmentPolicyDto = { ...INITIAL_MOCK_APPOINTMENT_POLICY };

export function getMockAppointmentPolicy(): AppointmentPolicyDto {
  return { ...currentMockPolicy };
}

export function updateMockAppointmentPolicy(
  payload: UpdateAppointmentPolicyRequest,
): AppointmentPolicyDto {
  currentMockPolicy = {
    ...payload,
    lastUpdatedAt: new Date().toISOString(),
  };
  return { ...currentMockPolicy };
}

export function resetMockAppointmentPolicy(): void {
  currentMockPolicy = { ...INITIAL_MOCK_APPOINTMENT_POLICY };
}
