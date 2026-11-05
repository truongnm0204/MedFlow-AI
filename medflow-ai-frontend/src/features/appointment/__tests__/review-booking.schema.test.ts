import { describe, expect, it } from 'vitest';

import {
  confirmBookingSchema,
  reviewBookingQuerySchema,
  reviewSymptomNoteSchema,
} from '../schemas/review-booking.schema';

describe('review-booking.schema', () => {
  it('validates a complete doctor-mode confirmBookingSchema payload', () => {
    const result = confirmBookingSchema.safeParse({
      mode: 'doctor',
      patientId: 'pat-self',
      doctorId: 'doc-01',
      serviceId: 'srv-th-01',
      serviceCode: 'DV-TH-01',
      dateIso: '2026-10-13',
      slotOrShiftId: 'slot-0900',
      symptomNote: 'Đau tức vùng thượng vị, ợ chua kéo dài.',
    });

    expect(result.success).toBe(true);
  });

  it('rejects service-mode confirmBookingSchema payload when symptomNote is missing or empty', () => {
    const result = confirmBookingSchema.safeParse({
      mode: 'service',
      patientId: 'pat-child',
      doctorId: 'doc-01',
      serviceId: 'srv-th-01',
      serviceCode: 'DV-TH-01',
      dateIso: '2026-10-14',
      slotOrShiftId: 'shift-morning',
      symptomNote: '   ',
    });

    expect(result.success).toBe(false);
  });

  it('rejects missing required fields and invalid date format', () => {
    const missingPatient = confirmBookingSchema.safeParse({
      mode: 'doctor',
      patientId: '   ',
      doctorId: 'doc-01',
      serviceId: 'srv-th-01',
      serviceCode: 'DV-TH-01',
      dateIso: '2026-10-13',
      slotOrShiftId: 'slot-0900',
    });
    expect(missingPatient.success).toBe(false);

    const badDate = confirmBookingSchema.safeParse({
      mode: 'doctor',
      patientId: 'pat-self',
      doctorId: 'doc-01',
      serviceId: 'srv-th-01',
      serviceCode: 'DV-TH-01',
      dateIso: '13/10/2026',
      slotOrShiftId: 'slot-0900',
    });
    expect(badDate.success).toBe(false);
  });

  it('rejects symptomNote exceeding 500 characters or shorter than 3 characters when editing', () => {
    const tooLong = 'a'.repeat(501);
    expect(reviewSymptomNoteSchema.safeParse({ symptomNote: tooLong }).success).toBe(false);
    expect(reviewSymptomNoteSchema.safeParse({ symptomNote: 'ab' }).success).toBe(false);
    expect(
      reviewSymptomNoteSchema.safeParse({ symptomNote: 'Đau bụng âm ỉ sau ăn 3 ngày' }).success,
    ).toBe(true);
  });

  it('defaults reviewBookingQuerySchema mode to doctor when omitted', () => {
    const parsed = reviewBookingQuerySchema.parse({});
    expect(parsed.mode).toBe('doctor');
  });
});
