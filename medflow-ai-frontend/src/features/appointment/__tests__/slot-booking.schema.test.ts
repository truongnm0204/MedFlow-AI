import { describe, expect, it } from 'vitest';

import { slotBookingSelectionSchema } from '../schemas/slot-booking.schema';

describe('slotBookingSelectionSchema', () => {
  it('validates a complete and valid slot booking selection', () => {
    const result = slotBookingSelectionSchema.safeParse({
      doctorId: 'doc-01',
      serviceCode: 'DV-TH-01',
      patientProfileId: 'pat-self',
      selectedDateIso: '2026-10-13',
      selectedSlotId: 'slot-0900',
      symptomNote: 'Đau tức vùng thượng vị, ợ chua sau ăn.',
    });

    expect(result.success).toBe(true);
  });

  it('rejects missing required fields', () => {
    const result = slotBookingSelectionSchema.safeParse({
      doctorId: '',
      serviceCode: 'DV-TH-01',
      patientProfileId: 'pat-self',
      selectedDateIso: '',
      selectedSlotId: '',
    });

    expect(result.success).toBe(false);
    if (!result.success) {
      const messages = result.error.issues.map((issue) => issue.message);
      expect(messages).toContain('Vui lòng chọn bác sĩ thăm khám');
      expect(messages).toContain('Vui lòng chọn ngày khám');
      expect(messages).toContain('Vui lòng chọn khung giờ thăm khám');
    }
  });

  it('rejects symptomNote exceeding 500 characters', () => {
    const result = slotBookingSelectionSchema.safeParse({
      doctorId: 'doc-01',
      serviceCode: 'DV-TH-01',
      patientProfileId: 'pat-self',
      selectedDateIso: '2026-10-13',
      selectedSlotId: 'slot-0900',
      symptomNote: 'a'.repeat(501),
    });

    expect(result.success).toBe(false);
  });
});
