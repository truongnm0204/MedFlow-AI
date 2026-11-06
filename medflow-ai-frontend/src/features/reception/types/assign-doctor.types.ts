export type WorkloadLevel = 'LOW' | 'MEDIUM' | 'HIGH';

export interface DoctorWorkloadItem {
  id: string;
  fullName: string;
  department: string;
  roomName: string;
  roomLocation: string;
  waitingCount: number;
  currentTicket: string;
  estimatedWait: string | null;
  workloadLevel: WorkloadLevel;
}

export interface AssignDoctorPayload {
  visitId: string;
  doctorId: string;
  roomName: string;
}

export interface VisitPatientSummary {
  visitId: string;
  fullName: string;
  gender: string;
  age: number;
  patientCode: string;
  cccdVerified: boolean;
  serviceName: string;
  serviceCode: string;
  price: number;
}
