import { describe, expect, it } from 'vitest';

import {
  appointmentListFilterSchema,
  validateAppointmentFilterInput,
} from '../schemas/appointment-list.schema';

describe('appointmentListFilterSchema', () => {
  it('parses default filter parameters when empty object is provided', () => {
    const result = appointmentListFilterSchema.parse({});
    expect(result).toEqual({
      memberId: 'ALL',
      tab: 'ALL',
      keyword: '',
      dateFrom: '',
      dateTo: '',
      page: 1,
      pageSize: 10,
    });
  });

  it('rejects keyword longer than 100 characters', () => {
    const result = validateAppointmentFilterInput({
      keyword: 'a'.repeat(101),
    });
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.errorMessage).toBe('Từ khóa tìm kiếm không được vượt quá 100 ký tự.');
    }
  });

  it('rejects when dateFrom is greater than dateTo', () => {
    const result = validateAppointmentFilterInput({
      dateFrom: '2026-10-30',
      dateTo: '2026-10-15',
    });
    expect(result.success).toBe(false);
    if (!result.success) {
      expect(result.errorMessage).toBe('Ngày bắt đầu không được lớn hơn ngày kết thúc.');
    }
  });

  it('accepts valid date range and trims keyword', () => {
    const result = validateAppointmentFilterInput({
      memberId: 'member-self',
      tab: 'UPCOMING',
      keyword: '  APT-2026-1013-0902  ',
      dateFrom: '2026-10-01',
      dateTo: '2026-10-31',
    });
    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.keyword).toBe('APT-2026-1013-0902');
      expect(result.data.tab).toBe('UPCOMING');
    }
  });
});
