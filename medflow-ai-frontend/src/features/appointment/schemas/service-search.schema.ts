import { z } from 'zod';

export const serviceCategorySchema = z.enum([
  'all',
  'specialty',
  'package',
  'pediatric',
  'general',
]);

export const serviceSearchParamsSchema = z.object({
  query: z.string().trim().max(100).optional(),
  category: serviceCategorySchema.default('all'),
  department: z.string().optional(),
  priceDurationRange: z.string().optional(),
  page: z.coerce.number().int().positive().default(1),
  pageSize: z.coerce.number().int().positive().max(50).default(6),
});

export type ServiceSearchParamsInput = z.infer<typeof serviceSearchParamsSchema>;

