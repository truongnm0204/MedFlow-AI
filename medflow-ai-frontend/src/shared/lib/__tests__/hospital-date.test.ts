import { describe, expect, it } from 'vitest';

import { formatHospitalTodayVn, getHospitalTodayIso, HOSPITAL_TIME_ZONE } from '../hospital-date';

describe('hospital-date (Asia/Ho_Chi_Minh)', () => {
  it('exposes Asia/Ho_Chi_Minh as hospital timezone', () => {
    expect(HOSPITAL_TIME_ZONE).toBe('Asia/Ho_Chi_Minh');
  });

  it('keeps current Vietnam calendar day right before midnight ICT (16:59:59.999Z)', () => {
    const beforeMidnightIct = new Date('2026-11-24T16:59:59.999Z');
    expect(getHospitalTodayIso(beforeMidnightIct)).toBe('2026-11-24');
    expect(formatHospitalTodayVn(beforeMidnightIct)).toBe('24/11/2026');
  });

  it('rolls over to the next Vietnam calendar day at 00:00:00 ICT (17:00:00.000Z)', () => {
    const atMidnightIct = new Date('2026-11-24T17:00:00.000Z');
    expect(getHospitalTodayIso(atMidnightIct)).toBe('2026-11-25');
    expect(formatHospitalTodayVn(atMidnightIct)).toBe('25/11/2026');
  });

  it('handles year boundary and leap-day transitions in Asia/Ho_Chi_Minh', () => {
    const newYearIct = new Date('2026-12-31T17:00:00.000Z');
    expect(getHospitalTodayIso(newYearIct)).toBe('2027-01-01');
    expect(formatHospitalTodayVn(newYearIct)).toBe('01/01/2027');

    const leapDayIct = new Date('2028-02-28T17:00:00.000Z');
    expect(getHospitalTodayIso(leapDayIct)).toBe('2028-02-29');
    expect(formatHospitalTodayVn(leapDayIct)).toBe('29/02/2028');
  });

  it('returns valid ISO and Vietnamese date formats when called without arguments', () => {
    expect(getHospitalTodayIso()).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(formatHospitalTodayVn()).toMatch(/^\d{2}\/\d{2}\/\d{4}$/);
  });
});
