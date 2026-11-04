import { describe, expect, it } from 'vitest';

import { INITIAL_DOCTORS } from '../data/doctor-demo-data';
import { doctorSchema } from '../schemas/doctor.schema';

const baseDoctor = INITIAL_DOCTORS[0];

describe('doctorSchema', () => {
  it('accepts a valid demo doctor and strips extra keys (portrait, history)', () => {
    const result = doctorSchema.safeParse(baseDoctor);

    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.id).toBe('DOC-0142');
      expect('portrait' in result.data).toBe(false);
      expect('history' in result.data).toBe(false);
    }
  });

  it.each(['DOC-142', 'doc-0142', 'DOC-01420'])(
    'rejects invalid practitioner id format "%s"',
    (invalidId) => {
      const result = doctorSchema.safeParse({ ...baseDoctor, id: invalidId });

      expect(result.success).toBe(false);
      if (!result.success) {
        expect(result.error.issues.map((i) => i.message)).toContain('Mã bác sĩ có dạng DOC-0000');
      }
    },
  );

  it('rejects invalid email address', () => {
    const result = doctorSchema.safeParse({ ...baseDoctor, email: 'abc' });

    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.error.issues.map((i) => i.message)).toContain('Email không hợp lệ');
    }
  });

  it.each([-1, 71])('rejects out-of-range experience %d', (experience) => {
    const result = doctorSchema.safeParse({ ...baseDoctor, experience });

    expect(result.success).toBe(false);
  });

  it.each([0, 70])('accepts boundary experience %d', (experience) => {
    const result = doctorSchema.safeParse({ ...baseDoctor, experience });

    expect(result.success).toBe(true);
  });

  it('accepts biography up to 1000 characters and rejects 1001 characters', () => {
    const validBio = doctorSchema.safeParse({ ...baseDoctor, biography: 'a'.repeat(1000) });
    expect(validBio.success).toBe(true);

    const invalidBio = doctorSchema.safeParse({ ...baseDoctor, biography: 'a'.repeat(1001) });
    expect(invalidBio.success).toBe(false);
    if (!invalidBio.success) {
      expect(invalidBio.error.issues.map((i) => i.message)).toContain('Tối đa 1000 ký tự');
    }
  });

  it('rejects license shorter than 5 chars and accepts 5 chars', () => {
    const tooShort = doctorSchema.safeParse({ ...baseDoctor, license: '1234' });
    expect(tooShort.success).toBe(false);
    if (!tooShort.success) {
      expect(tooShort.error.issues.map((i) => i.message)).toContain(
        'Vui lòng nhập số chứng chỉ hành nghề',
      );
    }

    const validLicense = doctorSchema.safeParse({ ...baseDoctor, license: '12345' });
    expect(validLicense.success).toBe(true);
  });
});
