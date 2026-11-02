import { useQuery } from '@tanstack/react-query';

import { fetchHospitalsApi } from '../api/hospital.api';
import type { Hospital } from '../types/booking-entry.types';

export function useHospitals() {
  return useQuery<Hospital[], Error>({
    queryKey: ['booking-hospitals'],
    queryFn: fetchHospitalsApi,
    staleTime: 5 * 60 * 1000,
  });
}
