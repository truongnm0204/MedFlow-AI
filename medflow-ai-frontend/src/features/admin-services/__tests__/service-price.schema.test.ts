import { describe, expect, it } from 'vitest';

import { updateServicePriceSchema } from '../schemas/service-price.schema';

describe('updateServicePriceSchema (UC52 / GH-818)', () => {
  const validData = {
    price: 350000,
    decisionNumber: 'QĐ-145/BV-KH',
    effectiveDate: '2026-05-01',
    appliedBy: 'Hội đồng Giá Bệnh viện',
    note: 'Điều chỉnh định mức viện phí năm 2026',
  };

  it('validates positive and zero (0 VND waived) price payloads', () => {
    const positive = updateServicePriceSchema.safeParse(validData);
    expect(positive.success).toBe(true);

    const zeroPrice = updateServicePriceSchema.safeParse({
      ...validData,
      price: 0,
      validTo: '2026-12-31',
    });
    expect(zeroPrice.success).toBe(true);
  });

  it('fails when price is negative or exceeds 1 billion VND', () => {
    const negativeResult = updateServicePriceSchema.safeParse({
      ...validData,
      price: -50000,
    });
    expect(negativeResult.success).toBe(false);

    const overflowResult = updateServicePriceSchema.safeParse({
      ...validData,
      price: 1_000_000_001,
    });
    expect(overflowResult.success).toBe(false);
  });

  it('fails when validTo is earlier than effectiveDate (ValidFrom <= ValidTo)', () => {
    const invalidRange = updateServicePriceSchema.safeParse({
      ...validData,
      effectiveDate: '2026-06-15',
      validTo: '2026-06-01',
    });
    expect(invalidRange.success).toBe(false);
  });

  it('fails when decisionNumber is shorter than 3 characters or effectiveDate format invalid', () => {
    expect(
      updateServicePriceSchema.safeParse({
        ...validData,
        decisionNumber: 'QD',
      }).success,
    ).toBe(false);

    expect(
      updateServicePriceSchema.safeParse({
        ...validData,
        effectiveDate: '01/05/2026',
      }).success,
    ).toBe(false);

    expect(
      updateServicePriceSchema.safeParse({
        ...validData,
        appliedBy: '   ',
      }).success,
    ).toBe(false);
  });
});
