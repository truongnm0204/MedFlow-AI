import { describe, expect, it } from 'vitest';

import { doctorFilterSchema } from '../schemas/doctor-search.schema';

describe('doctorFilterSchema', () => {
  it('parses empty object with valid defaults', () => {
    const result = doctorFilterSchema.safeParse({});
    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.page).toBe(1);
      expect(result.data.pageSize).toBe(6);
      expect(result.data.specialty).toBe('ALL');
      expect(result.data.degree).toBe('ALL');
      expect(result.data.hasScheduleToday).toBe(false);
      expect(result.data.hasScheduleThisWeek).toBe(false);
    }
  });

  it('trims keyword and validates max length', () => {
    const valid = doctorFilterSchema.safeParse({ keyword: '  Trần Quốc Tuấn  ' });
    expect(valid.success).toBe(true);
    if (valid.success) {
      expect(valid.data.keyword).toBe('Trần Quốc Tuấn');
    }

    const invalid = doctorFilterSchema.safeParse({
      keyword: 'a'.repeat(101),
    });
    expect(invalid.success).toBe(false);
  });

  it('rejects page numbers less than 1', () => {
    const result = doctorFilterSchema.safeParse({ page: 0 });
    expect(result.success).toBe(false);
  });

  it('coerces string numbers to integers for page and pageSize', () => {
    const result = doctorFilterSchema.safeParse({ page: '2', pageSize: '12' });
    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.page).toBe(2);
      expect(result.data.pageSize).toBe(12);
    }
  });
});
