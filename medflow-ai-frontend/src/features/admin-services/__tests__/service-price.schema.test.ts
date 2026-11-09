import { describe, expect, it } from 'vitest';

import { updateServicePriceSchema } from '../schemas/service-price.schema';

describe('updateServicePriceSchema (M3-207)', () => {
  const validData = {
    price: 350000,
    decisionNumber: 'QĐ-145/BV-KH',
    effectiveDate: '2026-05-01',
    appliedBy: 'Hội đồng Giá Bệnh viện',
    note: 'Điều chỉnh định mức viện phí năm 2026',
  };

  it('validates a correct price update payload', () => {
    const result = updateServicePriceSchema.safeParse(validData);
    expect(result.success).toBe(true);
    if (result.success) {
      expect(result.data.price).toBe(350000);
      expect(result.data.decisionNumber).toBe('QĐ-145/BV-KH');
      expect(result.data.appliedBy).toBe('Hội đồng Giá Bệnh viện');
    }
  });

  it('fails when price is zero or negative', () => {
    const zeroResult = updateServicePriceSchema.safeParse({
      ...validData,
      price: 0,
    });
    expect(zeroResult.success).toBe(false);

    const negativeResult = updateServicePriceSchema.safeParse({
      ...validData,
      price: -50000,
    });
    expect(negativeResult.success).toBe(false);
  });

  it('fails when price exceeds 1 billion VND', () => {
    const overflowResult = updateServicePriceSchema.safeParse({
      ...validData,
      price: 1_000_000_001,
    });
    expect(overflowResult.success).toBe(false);
  });

  it('fails when decisionNumber is shorter than 3 characters', () => {
    const result = updateServicePriceSchema.safeParse({
      ...validData,
      decisionNumber: 'QD',
    });
    expect(result.success).toBe(false);
  });

  it('fails when effectiveDate does not match YYYY-MM-DD format', () => {
    const result = updateServicePriceSchema.safeParse({
      ...validData,
      effectiveDate: '01/05/2026',
    });
    expect(result.success).toBe(false);
  });

  it('fails when appliedBy is empty or whitespace', () => {
    const result = updateServicePriceSchema.safeParse({
      ...validData,
      appliedBy: '   ',
    });
    expect(result.success).toBe(false);
  });

  it('allows optional note', () => {
    const withoutNote = {
      price: 400000,
      decisionNumber: 'QĐ-200/SYT',
      effectiveDate: '2026-06-01',
      appliedBy: 'Ban Giám đốc',
    };
    const result = updateServicePriceSchema.safeParse(withoutNote);
    expect(result.success).toBe(true);
  });
});
