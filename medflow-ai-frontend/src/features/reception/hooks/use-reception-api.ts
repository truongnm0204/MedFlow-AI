import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query';

import {
  assignDoctorToVisit,
  assignRoomToVisit,
  checkInScheduledPatient,
  createPatientAtReception,
  createWalkInVisit,
  fetchAssignableDoctors,
  fetchAvailableRooms,
  fetchPatientDetail,
  fetchWalkInCatalog,
  searchReceptionPatients,
  updatePatientAtReception,
} from '../api/reception.api';
import type { AssignDoctorPayload } from '../types/assign-doctor.types';
import type { CheckInFormData } from '../types/checkin.types';
import type { CreatePatientFormData } from '../types/create-patient.types';
import type { PatientEditableFields } from '../types/update-patient.types';
import type { ClinicalPriority, PaymentMethod, WalkInPatient } from '../types/walk-in-visit.types';

const searchKey = (query: string, tab: string) => ['reception', 'search', query, tab] as const;

export function useReceptionPatientSearch(query: string, tab: string) {
  return useQuery({
    queryKey: searchKey(query, tab),
    queryFn: () => searchReceptionPatients({ query, tab, page: 1, pageSize: 50 }),
    placeholderData: keepPreviousData,
  });
}

export function useCheckInMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { appointmentIdOrCode: string; form: CheckInFormData }) =>
      checkInScheduledPatient(input.appointmentIdOrCode, input.form),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['reception', 'search'] }),
  });
}

export function useWalkInCatalog() {
  return useQuery({
    queryKey: ['reception', 'walk-in', 'catalog'],
    queryFn: fetchWalkInCatalog,
    staleTime: 60_000,
  });
}

export function useCreateWalkInMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: {
      patient: WalkInPatient;
      departmentId: string;
      serviceId: string;
      reasonForVisit: string;
      symptoms: string[];
      clinicalPriority: ClinicalPriority;
      paymentMethod: PaymentMethod;
      serviceName: string;
      priceFormatted: string;
    }) => createWalkInVisit(input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['reception', 'search'] }),
  });
}

export function useAssignableDoctors(visitIdOrCode: string) {
  return useQuery({
    queryKey: ['reception', 'visit', visitIdOrCode, 'doctors'],
    queryFn: () => fetchAssignableDoctors(visitIdOrCode),
    enabled: Boolean(visitIdOrCode),
  });
}

export function useAssignDoctorMutation(visitIdOrCode: string) {
  return useMutation({
    mutationFn: (payload: AssignDoctorPayload) => assignDoctorToVisit(visitIdOrCode, payload),
  });
}

export function useAvailableRooms(visitIdOrCode: string) {
  return useQuery({
    queryKey: ['reception', 'visit', visitIdOrCode, 'rooms'],
    queryFn: () => fetchAvailableRooms(visitIdOrCode),
    enabled: Boolean(visitIdOrCode),
  });
}

export function useAssignRoomMutation(visitIdOrCode: string) {
  return useMutation({
    mutationFn: (roomId: string) => assignRoomToVisit(visitIdOrCode, roomId),
  });
}

export function usePatientDetail(idOrCode: string | null) {
  return useQuery({
    queryKey: ['reception', 'patient', idOrCode],
    queryFn: () => fetchPatientDetail(idOrCode!),
    enabled: Boolean(idOrCode),
  });
}

export function useUpdatePatientMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { idOrCode: string; values: PatientEditableFields }) =>
      updatePatientAtReception(input.idOrCode, input.values),
    onSuccess: (_data, input) => {
      queryClient.invalidateQueries({ queryKey: ['reception', 'patient', input.idOrCode] });
      queryClient.invalidateQueries({ queryKey: ['reception', 'search'] });
    },
  });
}

export function useCreatePatientMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreatePatientFormData) => createPatientAtReception(data),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['reception', 'search'] }),
  });
}
