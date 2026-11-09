export type ExaminationTicketGender = 'MALE' | 'FEMALE' | 'OTHER';

export interface ExaminationTicketData {
  hospitalName: string;
  ticketNumber: string;
  roomName: string;
  departmentName: string;
  location: string;
  doctorName: string;
  patientName: string;
  gender: ExaminationTicketGender;
  age: number;
  patientCode: string;
  visitCode: string;
  serviceName: string;
  receivedAt: string;
  expectedTimeRange: string;
  barcodeValue: string;
  footerNote: string;
}
