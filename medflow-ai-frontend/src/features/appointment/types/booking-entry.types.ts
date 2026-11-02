export type BookingMode = 'DOCTOR' | 'SERVICE';

/** Domain model cho một cơ sở khám của MedFlow (hospitals table). */
export interface Hospital {
  id: string;
  hospitalCode: string;
  name: string;
  address: string;
  isActive: boolean;
}

/** Flat DTO của `GET /api/v1/hospitals` (backend chưa ship — ADR-0006). */
export interface HospitalDto {
  id: number | string;
  hospitalCode: string | null;
  name: string | null;
  address: string | null;
  isActive: boolean | null;
}
