export type ClinicalPriority = 'STANDARD' | 'PRIORITY' | 'EMERGENCY';

export type PaymentMethod = 'VIETQR' | 'CASHIER';

export interface WalkInPatient {
  id: string;
  patientCode: string;
  fullName: string;
  gender: 'MALE' | 'FEMALE' | 'OTHER';
  birthYear: number;
  identityCard: string;
  phoneNumber: string;
  insuranceType: string;
}

export interface WalkInService {
  id: string;
  code: string;
  name: string;
  price: number;
  priceFormatted: string;
}

export interface WalkInDepartment {
  id: string;
  name: string;
  location: string;
  services: WalkInService[];
}

export interface CreateWalkInVisitPayload {
  patientId: string;
  clinicalPriority: ClinicalPriority;
  departmentId: string;
  serviceId: string;
  reasonForVisit: string;
  symptoms: string[];
  paymentMethod: PaymentMethod;
}

export interface CreatedWalkInVisitResult {
  visitId: string;
  visitCode: string;
  ticketNo: string;
  patientName: string;
  departmentName: string;
  serviceName: string;
  receptionTime: string;
  paymentMethod: PaymentMethod;
  priceFormatted: string;
}
