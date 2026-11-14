import { apiClient } from '@/shared/lib/axios';

import type {
  AssignDoctorPayload,
  DoctorWorkloadItem,
  VisitPatientSummary,
} from '../types/assign-doctor.types';
import type { AvailableRoomItem } from '../types/assign-room.types';
import type { CheckInFormData, CheckInSuccessResult } from '../types/checkin.types';
import type { PatientSearchResultItem } from '../types/patient-search.types';
import type {
  ClinicalPriority,
  CreatedWalkInVisitResult,
  PaymentMethod,
  WalkInDepartment,
  WalkInPatient,
} from '../types/walk-in-visit.types';

// --- Search ----------------------------------------------------------------

interface TodayAppointmentDto {
  id: number;
  time: string;
  doctorName: string;
  department: string;
  roomName: string;
  status: string;
}

interface PatientSearchItemDto {
  id: number;
  patientCode: string;
  fullName: string;
  gender: string | null;
  dateOfBirth: string | null;
  identityCardMasked: string | null;
  phone: string | null;
  address: string | null;
  todayAppointment: TodayAppointmentDto | null;
  totalPastVisits: number;
}

interface SearchPatientsResponseDto {
  items: PatientSearchItemDto[];
  totalCount: number;
  page: number;
  pageSize: number;
  counts: { all: number; hasAppointment: number; checkedIn: number; noAppointment: number };
}

export interface ReceptionSearchParams {
  query?: string;
  tab?: string;
  page?: number;
  pageSize?: number;
}

export interface ReceptionSearchResult {
  items: PatientSearchResultItem[];
  counts: { all: number; hasAppointment: number; checkedIn: number; noAppointment: number };
  totalCount: number;
}

const APPOINTMENT_STATUS_MAP: Record<
  string,
  'BOOKED' | 'CHECKED_IN' | 'CANCELLED' | 'IN_CONSULTATION'
> = {
  BOOKED: 'BOOKED',
  CONFIRMED: 'BOOKED',
  PENDING: 'BOOKED',
  CHECKED_IN: 'CHECKED_IN',
  CANCELLED: 'CANCELLED',
  NO_SHOW: 'CANCELLED',
  IN_CONSULTATION: 'IN_CONSULTATION',
  IN_PROGRESS: 'IN_CONSULTATION',
};

export function mapSearchItem(dto: PatientSearchItemDto): PatientSearchResultItem {
  const appointment = dto.todayAppointment;
  return {
    id: String(dto.id),
    patientCode: dto.patientCode,
    fullName: dto.fullName,
    gender: dto.gender === 'MALE' || dto.gender === 'FEMALE' ? dto.gender : 'OTHER',
    dateOfBirth: dto.dateOfBirth ?? '',
    identityCard: dto.identityCardMasked ?? '',
    phoneNumber: dto.phone ?? '',
    healthInsuranceNumber: undefined,
    address: dto.address ?? '',
    totalPastVisits: dto.totalPastVisits,
    todayAppointment: appointment
      ? {
          id: String(appointment.id),
          time: appointment.time,
          doctorName: appointment.doctorName,
          department: appointment.department,
          roomName: appointment.roomName,
          status: APPOINTMENT_STATUS_MAP[appointment.status?.toUpperCase()] ?? 'BOOKED',
        }
      : undefined,
  };
}

export async function searchReceptionPatients(
  params: ReceptionSearchParams,
): Promise<ReceptionSearchResult> {
  const { data } = await apiClient.get<SearchPatientsResponseDto>('/reception/patients/search', {
    params: {
      query: params.query ?? '',
      tab: params.tab ?? 'ALL',
      page: params.page ?? 1,
      pageSize: params.pageSize ?? 20,
    },
  });
  return {
    items: (data.items ?? []).map(mapSearchItem),
    counts: data.counts ?? { all: 0, hasAppointment: 0, checkedIn: 0, noAppointment: 0 },
    totalCount: data.totalCount ?? 0,
  };
}

// --- Check-in --------------------------------------------------------------

interface CheckInResponseDto {
  visitId: number;
  visitCode: string;
  ticketNo: string;
  queueNumber: number;
  priority: string;
  patientCode: string;
  patientName: string;
  departmentName: string;
  roomName: string;
  doctorName: string;
  waitingCountBefore: number;
  estimatedWaitMinutes: number;
  checkedInAt: string;
  nextStepInstructions: string[];
}

const PRIORITIES: Record<string, true> = {
  NORMAL: true,
  URGENT: true,
  ELDERLY: true,
  PREGNANT: true,
};

function mapCheckInResponse(data: CheckInResponseDto): CheckInSuccessResult {
  return {
    visitId: String(data.visitId),
    visitCode: data.visitCode,
    ticketNo: data.ticketNo,
    patientName: data.patientName,
    patientCode: data.patientCode,
    roomName: data.roomName,
    departmentName: data.departmentName,
    doctorName: data.doctorName,
    checkedInAt: data.checkedInAt,
    priority:
      data.priority && PRIORITIES[data.priority.toUpperCase()] === true
        ? (data.priority.toUpperCase() as CheckInSuccessResult['priority'])
        : 'NORMAL',
    queueNumber: data.queueNumber,
    waitingCountBefore: data.waitingCountBefore,
    estimatedWaitMinutes: data.estimatedWaitMinutes,
    nextStepInstructions: data.nextStepInstructions ?? [],
  };
}

export async function checkInScheduledPatient(
  appointmentIdOrCode: string,
  form: CheckInFormData,
): Promise<CheckInSuccessResult> {
  const { data } = await apiClient.post<CheckInResponseDto>(
    `/reception/appointments/${encodeURIComponent(appointmentIdOrCode)}/check-in`,
    {
      reasonForVisit: form.reasonForVisit,
      bloodPressure: form.bloodPressure,
      heartRate: form.heartRate,
      temperature: form.temperature,
      isIdentityVerified: form.isIdentityVerified,
      isInsuranceVerified: form.isInsuranceVerified,
      priority: form.priority,
    },
  );

  return mapCheckInResponse(data);
}

export async function fetchCheckInResult(
  appointmentIdOrCode: string,
): Promise<CheckInSuccessResult> {
  const { data } = await apiClient.get<CheckInResponseDto>(
    `/reception/appointments/${encodeURIComponent(appointmentIdOrCode)}/check-in-result`,
  );
  return mapCheckInResponse(data);
}

// --- Walk-in ---------------------------------------------------------------

interface WalkInServiceDto {
  id: number;
  code: string;
  name: string;
  price: number;
  priceFormatted: string;
}

interface WalkInDepartmentDto {
  id: number;
  code: string;
  name: string;
  location: string;
  services: WalkInServiceDto[];
}

interface WalkInCatalogDto {
  hospitalId: number;
  hospitalName: string;
  departments: WalkInDepartmentDto[];
  symptomOptions: string[];
}

export interface WalkInCatalog {
  departments: WalkInDepartment[];
  symptomOptions: string[];
}

export async function fetchWalkInCatalog(): Promise<WalkInCatalog> {
  const { data } = await apiClient.get<WalkInCatalogDto>('/reception/walk-in/catalog');
  return {
    symptomOptions: data.symptomOptions ?? [],
    departments: (data.departments ?? []).map((dept) => ({
      id: String(dept.id),
      name: dept.name,
      location: dept.location,
      services: (dept.services ?? []).map((service) => ({
        id: String(service.id),
        code: service.code,
        name: service.name,
        price: service.price,
        priceFormatted: service.priceFormatted,
      })),
    })),
  };
}

const CLINICAL_PRIORITY_MAP: Record<ClinicalPriority, string> = {
  STANDARD: 'NORMAL',
  PRIORITY: 'ELDERLY',
  EMERGENCY: 'URGENT',
};

interface CreateWalkInResponseDto {
  visitId: number;
  visitCode: string;
  ticketNo: string;
  patientName: string;
  departmentName: string;
  checkedInAt: string;
}

export async function createWalkInVisit(input: {
  patient: WalkInPatient;
  departmentId: string;
  serviceId: string;
  reasonForVisit: string;
  symptoms: string[];
  clinicalPriority: ClinicalPriority;
  paymentMethod: PaymentMethod;
  serviceName: string;
  priceFormatted: string;
}): Promise<CreatedWalkInVisitResult> {
  const { data } = await apiClient.post<CreateWalkInResponseDto>('/reception/visits/walk-in', {
    patientId: input.patient.id,
    patientCode: input.patient.patientCode,
    departmentId: input.departmentId,
    serviceId: input.serviceId,
    reasonForVisit: input.reasonForVisit,
    symptoms: input.symptoms,
    clinicalPriority: CLINICAL_PRIORITY_MAP[input.clinicalPriority],
    paymentMethod: input.paymentMethod,
    isIdentityVerified: true,
  });

  return {
    visitId: String(data.visitId),
    visitCode: data.visitCode,
    ticketNo: data.ticketNo,
    patientName: data.patientName,
    departmentName: data.departmentName,
    serviceName: input.serviceName,
    receptionTime: data.checkedInAt,
    paymentMethod: input.paymentMethod,
    priceFormatted: input.priceFormatted,
  };
}

// --- Doctor assignment -----------------------------------------------------

interface AssignableDoctorDto {
  doctorId: number;
  fullName: string;
  departmentName: string | null;
  department: string;
  roomName: string;
  roomLocation: string;
  waitingCount: number;
  currentTicket: string;
  estimatedWait: string | null;
  workloadLevel: string;
}

interface PatientSummaryDto {
  visitNumericId: number;
  fullName: string;
  gender: string;
  age: number;
  patientCode: string;
  cccdVerified: boolean;
  serviceName: string;
  serviceCode: string;
  price: number;
}

interface AssignableDoctorListDto {
  visitId: number;
  visitCode: string;
  patient: PatientSummaryDto;
  doctors: AssignableDoctorDto[];
}

export interface AssignableDoctorsResult {
  visitCode: string;
  patient: VisitPatientSummary;
  doctors: DoctorWorkloadItem[];
}

const WORKLOAD: Record<string, true> = { LOW: true, MEDIUM: true, HIGH: true };

export async function fetchAssignableDoctors(
  visitIdOrCode: string,
): Promise<AssignableDoctorsResult> {
  const { data } = await apiClient.get<AssignableDoctorListDto>(
    `/reception/visits/${encodeURIComponent(visitIdOrCode)}/available-doctors`,
  );

  return {
    visitCode: data.visitCode,
    patient: {
      visitId: String(data.patient?.visitNumericId ?? data.visitId),
      fullName: data.patient?.fullName ?? '',
      gender: data.patient?.gender ?? '',
      age: data.patient?.age ?? 0,
      patientCode: data.patient?.patientCode ?? '',
      cccdVerified: Boolean(data.patient?.cccdVerified),
      serviceName: data.patient?.serviceName ?? '',
      serviceCode: data.patient?.serviceCode ?? '',
      price: data.patient?.price ?? 0,
    },
    doctors: (data.doctors ?? []).map((doctor) => ({
      id: String(doctor.doctorId),
      fullName: doctor.fullName,
      department: doctor.departmentName ?? doctor.department,
      roomName: doctor.roomName,
      roomLocation: doctor.roomLocation,
      waitingCount: doctor.waitingCount,
      currentTicket: doctor.currentTicket,
      estimatedWait: doctor.estimatedWait,
      workloadLevel:
        doctor.workloadLevel && WORKLOAD[doctor.workloadLevel.toUpperCase()] === true
          ? (doctor.workloadLevel.toUpperCase() as DoctorWorkloadItem['workloadLevel'])
          : 'MEDIUM',
    })),
  };
}

export async function assignDoctorToVisit(
  visitIdOrCode: string,
  payload: AssignDoctorPayload,
): Promise<{ doctorName: string; roomName: string; visitCode: string }> {
  const { data } = await apiClient.put<{
    visitCode: string;
    assignedDoctorName: string;
    roomName: string | null;
  }>(`/reception/visits/${encodeURIComponent(visitIdOrCode)}/doctor`, {
    doctorId: Number(payload.doctorId),
  });
  return {
    doctorName: data.assignedDoctorName,
    roomName: data.roomName ?? payload.roomName,
    visitCode: data.visitCode,
  };
}

// --- Room assignment -------------------------------------------------------

interface RoomListDto {
  visitCode: string;
  requiredDepartmentName: string | null;
  rooms: Array<{
    id: number;
    roomCode: string;
    name: string;
    location: string | null;
    capacity: number | null;
    currentOccupancy: number;
    availableCapacity: number;
    isCompatible: boolean;
    isAvailable: boolean;
    unavailableReason: string | null;
  }>;
}

export interface AvailableRoomsResult {
  visitCode: string;
  departmentName: string | null;
  rooms: AvailableRoomItem[];
}

export async function fetchAvailableRooms(visitIdOrCode: string): Promise<AvailableRoomsResult> {
  const { data } = await apiClient.get<RoomListDto>(
    `/reception/visits/${encodeURIComponent(visitIdOrCode)}/available-rooms`,
  );
  return {
    visitCode: data.visitCode,
    departmentName: data.requiredDepartmentName ?? null,
    rooms: (data.rooms ?? []).map((room) => ({
      id: String(room.id),
      code: room.roomCode,
      name: room.name,
      location: room.location ?? '',
      status:
        !room.isCompatible || !room.isAvailable
          ? 'BUSY'
          : room.availableCapacity <= 0
            ? 'OVERLOADED'
            : 'READY',
      capacityPerSlot: room.capacity ?? undefined,
      waitingCount: room.currentOccupancy,
      compatibilityPercent: room.isCompatible ? 100 : 0,
    })),
  };
}

export async function assignRoomToVisit(
  visitIdOrCode: string,
  roomId: string,
): Promise<{ roomName: string; queueNumber: number | null }> {
  const { data } = await apiClient.put<{
    roomName: string;
    queueNumber: number | null;
  }>(`/reception/visits/${encodeURIComponent(visitIdOrCode)}/examination-room`, {
    examinationRoomId: Number(roomId),
  });
  return { roomName: data.roomName, queueNumber: data.queueNumber };
}
