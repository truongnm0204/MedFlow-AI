import { z } from 'zod';

export function policyIntField(label: string, min: number, max: number) {
  return z
    .number({ message: `${label} là bắt buộc.` })
    .int(`${label} phải là số nguyên.`)
    .min(min, `${label} phải từ ${min} đến ${max}.`)
    .max(max, `${label} phải từ ${min} đến ${max}.`);
}
