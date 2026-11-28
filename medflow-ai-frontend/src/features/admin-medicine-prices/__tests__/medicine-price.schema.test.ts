import { describe, expect, it } from 'vitest';

import { updateMedicinePriceSchema } from '../schemas/medicine-price.schema';

describe('updateMedicinePriceSchema (GH-865)', () => {
  const validData = {
    price: 1500,
    decisionNumber: 'QĐ-865/BV-KH',
    effectiveDate: '2026-05-01',
    appliedBy: 'Hội đồng Thuốc & Điều trị',
    note: 'Điều chỉnh giá thuốc theo quyết định trúng thầu',
  };

  it('validates a valid payload and allows zero price', () => {
    expect(updateMedicinePriceSchema.safeParse(validData).success).toBe(true);
    expect(
      updateMedicinePriceSchema.safeParse({
        ...validData,
        price: 0,
        validTo: '2026-12-31',
      }).success,
    ).toBe(true);
  });

  it('fails when price is negative or exceeds 1 billion VND', () => {
    expect(updateMedicinePriceSchema.safeParse({ ...validData, price: -1 }).success).toBe(false);
    expect(
      updateMedicinePriceSchema.safeParse({ ...validData, price: 1_000_000_001 }).success,
    ).toBe(false);
  });

  it('fails when validTo is earlier than effectiveDate', () => {
    expect(
      updateMedicinePriceSchema.safeParse({
        ...validData,
        effectiveDate: '2026-06-15',
        validTo: '2026-06-01',
      }).success,
    ).toBe(false);
  });

  it('fails when decisionNumber is too short or appliedBy is missing', () => {
    expect(
      updateMedicinePriceSchema.safeParse({ ...validData, decisionNumber: 'QĐ' }).success,
    ).toBe(false);
    expect(updateMedicinePriceSchema.safeParse({ ...validData, appliedBy: '   ' }).success).toBe(
      false,
    );
  });
});
