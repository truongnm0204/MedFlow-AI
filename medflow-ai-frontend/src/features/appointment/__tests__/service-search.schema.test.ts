import { describe, expect, it } from 'vitest';
import { serviceSearchParamsSchema } from '../schemas/service-search.schema';

describe('serviceSearchParamsSchema', () => {
  it('parses empty input with defaults', () => {
    const result = serviceSearchParamsSchema.safeParse({});
    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.category).toBe('all');
      expect(result.data.page).toBe(1);
      expect(result.data.pageSize).toBe(6);
    }
  });

  it('validates correct categories', () => {
    const validCategories = ['all', 'specialty', 'package', 'pediatric', 'general'] as const;
    for (const cat of validCategories) {
      const res = serviceSearchParamsSchema.safeParse({ category: cat });
      expect(res.success).toBe(true);
    }
  });

  it('rejects invalid category', () => {
    const res = serviceSearchParamsSchema.safeParse({ category: 'invalid-cat' });
    expect(res.success).toBe(false);
  });

  it('coerces string numbers for page and pageSize', () => {
    const res = serviceSearchParamsSchema.safeParse({
      page: '2',
      pageSize: '10',
    });
    expect(res.success).toBe(true);
    if (res.success) {
      expect(res.data.page).toBe(2);
      expect(res.data.pageSize).toBe(10);
    }
  });
});
