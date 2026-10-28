import { z } from 'zod';

export const doctorFilterSchema = z.object({
  keyword: z.string().trim().max(100).optional().default(''),
  specialty: z.string().default('ALL'),
  degree: z.string().default('ALL'),
  hasScheduleToday: z.boolean().default(false),
  hasScheduleThisWeek: z.boolean().default(false),
  page: z.coerce.number().int().min(1).default(1),
  pageSize: z.coerce.number().int().min(1).max(50).default(6),
});

export type DoctorFilterFormValues = z.infer<typeof doctorFilterSchema>;
