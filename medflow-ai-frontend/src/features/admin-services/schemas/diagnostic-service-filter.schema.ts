import { z } from 'zod';

export const diagnosticServiceKeywordSchema = z
  .string()
  .trim()
  .max(100, 'Từ khóa tìm kiếm tối đa 100 ký tự.');
