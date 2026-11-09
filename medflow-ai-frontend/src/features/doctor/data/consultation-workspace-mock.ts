import {
  calculateClinicalBmi,
  formatBloodPressureLabel,
} from '../schemas/consultation-workspace.schema';
import type {
  ConsultationDiagnosticSummaryDto,
  ConsultationWorkspaceActionDto,
  ConsultationWorkspaceResponseDto,
  ConsultationWorkspaceState,
  OpenConsultationWorkspaceRequest,
  RecordClinicalExaminationRequest,
} from '../types/consultation-workspace.types';
import { DOCTOR_WORKBENCH_META } from './doctor-mock-data';

function buildAllowedActions(
  workspaceState: ConsultationWorkspaceState,
  canEditClinical: boolean,
  isRecordFinalized: boolean,
  diagnosticSummary: ConsultationDiagnosticSummaryDto,
): ConsultationWorkspaceActionDto[] {
  const canOpenConsultation =
    workspaceState === 'CALLED' && diagnosticSummary.allMandatoryResultsReady;

  let openDisabledReason: string | null = null;
  if (!canOpenConsultation) {
    if (workspaceState === 'IN_PROGRESS') {
      openDisabledReason = 'Phiên khám lâm sàng đang diễn ra (IN_PROGRESS).';
    } else if (workspaceState === 'COMPLETED') {
      openDisabledReason = 'Lượt khám đã hoàn tất (COMPLETED).';
    } else if (!diagnosticSummary.allMandatoryResultsReady) {
      openDisabledReason = 'Chưa có đủ kết quả cận lâm sàng bắt buộc để mở phiên khám lại.';
    } else {
      openDisabledReason = 'Vui lòng gọi bệnh nhân vào phòng (CALLED) trước khi bắt đầu khám.';
    }
  }

  let clinicalDisabledReason: string | null = null;
  if (!canEditClinical) {
    if (isRecordFinalized) {
      clinicalDisabledReason = 'Hồ sơ bệnh án đã được khóa hoàn tất (FINALIZED).';
    } else if (workspaceState === 'COMPLETED') {
      clinicalDisabledReason = 'Lượt khám đã hoàn tất (COMPLETED).';
    } else if (workspaceState === 'AWAITING_RESULTS') {
      clinicalDisabledReason =
        'Bệnh nhân đang chờ kết quả cận lâm sàng. Vui lòng gọi bệnh nhân vào khám lại sau khi có kết quả.';
    } else {
      clinicalDisabledReason =
        'Bệnh nhân đang ở trạng thái chờ/đã gọi. Vui lòng bấm Bắt đầu khám (IN_PROGRESS) để ghi nhận lâm sàng.';
    }
  }

  const canCreateDiagnosticOrder = workspaceState === 'IN_PROGRESS' && !isRecordFinalized;
  const diagnosticOrderDisabledReason = canCreateDiagnosticOrder
    ? null
    : (clinicalDisabledReason ??
      'Chỉ có thể chỉ định cận lâm sàng khi ca khám đang diễn ra (IN_PROGRESS).');

  const canRecordDiagnosis = canEditClinical && diagnosticSummary.allMandatoryResultsReady;
  const diagnosisDisabledReason = canRecordDiagnosis
    ? null
    : !diagnosticSummary.allMandatoryResultsReady
      ? 'Chưa thể kết luận chẩn đoán khi kết quả cận lâm sàng bắt buộc chưa hoàn tất.'
      : clinicalDisabledReason;

  const canCreatePrescription = canEditClinical && diagnosticSummary.allMandatoryResultsReady;
  const prescriptionDisabledReason = canCreatePrescription
    ? null
    : !diagnosticSummary.allMandatoryResultsReady
      ? 'Chưa thể kê đơn thuốc khi kết quả cận lâm sàng bắt buộc chưa hoàn tất.'
      : clinicalDisabledReason;

  const canCompleteVisit = canEditClinical && diagnosticSummary.allMandatoryResultsReady;
  const completeVisitDisabledReason = canCompleteVisit
    ? null
    : !diagnosticSummary.allMandatoryResultsReady
      ? 'Chưa thể hoàn tất lượt khám khi kết quả cận lâm sàng bắt buộc chưa hoàn tất.'
      : clinicalDisabledReason;

  return [
    {
      id: 'open-consultation',
      label: 'Bắt đầu khám lâm sàng',
      ucCode: 'UC31',
      enabled: canOpenConsultation,
      disabledReason: openDisabledReason,
    },
    {
      id: 'view-medical-history',
      label: 'Xem tiền sử & lịch sử khám',
      ucCode: 'UC32',
      enabled: true,
      disabledReason: null,
    },
    {
      id: 'record-clinical-examination',
      label: 'Ghi nhận sinh hiệu & khám lâm sàng',
      ucCode: 'UC33',
      enabled: canEditClinical,
      disabledReason: clinicalDisabledReason,
    },
    {
      id: 'create-diagnostic-order',
      label: 'Chỉ định cận lâm sàng',
      ucCode: 'UC34',
      enabled: canCreateDiagnosticOrder,
      disabledReason: diagnosticOrderDisabledReason,
    },
    {
      id: 'record-diagnosis',
      label: 'Ghi nhận chẩn đoán & kết luận',
      ucCode: 'UC36',
      enabled: canRecordDiagnosis,
      disabledReason: diagnosisDisabledReason,
    },
    {
      id: 'create-prescription',
      label: 'Kê đơn thuốc ngoại trú',
      ucCode: 'UC37',
      enabled: canCreatePrescription,
      disabledReason: prescriptionDisabledReason,
    },
    {
      id: 'complete-visit',
      label: 'Hoàn tất lượt khám',
      ucCode: 'UC38',
      enabled: canCompleteVisit,
      disabledReason: completeVisitDisabledReason,
    },
  ];
}

const COMMON_HOSPITAL = {
  hospitalId: 1,
  hospitalCode: DOCTOR_WORKBENCH_META.facilityCode,
  hospitalName: DOCTOR_WORKBENCH_META.facilityName,
  departmentId: 2,
  departmentCode: 'DEP-NOI-01',
  departmentName: DOCTOR_WORKBENCH_META.departmentName,
  specialtyId: 1,
  specialtyCode: 'SPC-NOI-TQ',
  specialtyName: 'Nội tổng quát',
  roomId: 102,
  roomCode: DOCTOR_WORKBENCH_META.roomCode,
  roomName: 'Phòng khám Nội tổng quát 102',
  roomLocation: 'Tầng 1 • Khu Khám bệnh A',
  assignedDoctorId: 1,
  assignedDoctorCode: 'DOC-001',
  assignedDoctorName: DOCTOR_WORKBENCH_META.defaultDoctorName,
  assignedDoctorTitle: 'BS.CKII',
  serviceId: 10,
  serviceCode: 'SRV-NOI-01',
  serviceName: 'Khám Nội tổng quát chuyên sâu',
} as const;

const READY_DIAGNOSTIC_SUMMARY: ConsultationDiagnosticSummaryDto = {
  totalQueueEntries: 0,
  openQueueEntries: 0,
  completedQueueEntries: 0,
  allMandatoryResultsReady: true,
  finalResultRequired: true,
};

const PENDING_DIAGNOSTIC_SUMMARY: ConsultationDiagnosticSummaryDto = {
  totalQueueEntries: 2,
  openQueueEntries: 1,
  completedQueueEntries: 1,
  allMandatoryResultsReady: false,
  finalResultRequired: true,
};

const COMPLETED_DIAGNOSTIC_SUMMARY: ConsultationDiagnosticSummaryDto = {
  totalQueueEntries: 1,
  openQueueEntries: 0,
  completedQueueEntries: 1,
  allMandatoryResultsReady: true,
  finalResultRequired: true,
};

export const CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK: ConsultationWorkspaceResponseDto = {
  actionOutcome: 'VIEWED',
  workspaceState: 'IN_PROGRESS',
  canEditClinical: true,
  readOnlyReason: null,
  generatedAtUtc: '2026-11-07T02:10:00Z',
  visit: {
    ...COMMON_HOSPITAL,
    visitId: 212,
    visitCode: 'VIS-2026-0212',
    status: 'IN_PROGRESS',
    statusLabel: 'Đang khám lâm sàng',
    visitType: 'SCHEDULED',
    visitTypeLabel: 'Đặt lịch hẹn',
    reasonForVisit: 'Đau tức ngực trái lan lên vai, khó thở khi gắng sức',
    appointmentId: 501,
    appointmentCode: 'APT-20261107-0212',
    scheduledStartAt: '2026-11-07T08:00:00',
    scheduledEndAt: '2026-11-07T08:30:00',
    checkedInAt: '2026-11-07T07:50:00',
    startedAt: '2026-11-07T08:05:00',
    completedAt: null,
  },
  patient: {
    patientId: 101,
    patientCode: 'BN-2026-0212',
    fullName: 'Nguyễn Văn An',
    dateOfBirth: '1968-04-15',
    birthYear: 1968,
    age: 58,
    gender: 'MALE',
    genderLabel: 'Nam',
    phone: '0901234567',
    email: 'nguyenvanan@example.com',
    address: '124 Nguyễn Trãi, Quận 1, TP. Hồ Chí Minh',
    bloodType: 'O+',
    allergiesSummary: 'Dị ứng Penicillin (nổi mề đay), tiền sử tăng huyết áp 5 năm',
  },
  queue: {
    queueEntryId: 12,
    formattedQueueEntryId: 'q-012',
    ticketNo: 'A012',
    queueNumber: 12,
    queueDate: '2026-11-07',
    queueType: 'EXAMINATION',
    status: 'IN_PROGRESS',
    queueGroup: 'IN_PROGRESS',
    ineligibleReason: null,
    priority: 'NORMAL',
    presenceStatus: 'PRESENT',
    arrivalClass: 'ON_TIME_OR_GRACE',
    servingOrder: 1,
    isEligible: false,
    isAppointmentProtected: true,
    holdReason: null,
    blockedReason: null,
    stateVersion: 3,
    servicePointId: 'sp-room-102',
    servicePointName: 'Phòng khám Nội tổng quát 102',
    scheduledAt: '08:00',
    arrivedAt: '07:50',
    readyAt: '07:52',
    calledAt: '08:02',
    startedAt: '08:05',
    completedAt: null,
  },
  clinicalContext: {
    medicalRecordId: 801,
    medicalRecordStatus: 'DRAFT',
    finalizedAt: null,
    currentRevisionNo: 1,
    isConfirmed: false,
    lastRecordedAt: '2026-11-07T08:06:00',
    presentingComplaint: 'Đau tức ngực trái lan lên vai, khó thở khi gắng sức',
    symptomHistory:
      'Khởi phát đau âm ỉ vùng sau xương ức cách đây 3 ngày khi leo cầu thang, nghỉ ngơi 10 phút giảm đau.',
    relevantHistoryAllergies: 'Dị ứng Penicillin (nổi mề đay), tiền sử tăng huyết áp 5 năm',
    vitalSigns: {
      bpSystolicMmhg: 145,
      bpDiastolicMmhg: 90,
      bloodPressureLabel: '145/90 mmHg',
      pulseBpm: 88,
      temperatureC: 36.8,
      spo2Percent: 97,
      heightCm: 168,
      weightKg: 68.3,
      bmi: 24.2,
    },
    clinicalFindings:
      'Bệnh nhân tỉnh, tiếp xúc tốt. Tim đều 88 lần/phút, T1 T2 rõ, không âm thổi bệnh lý. Phổi thông khí đều hai bên.',
    clinicalConclusion: 'Theo dõi cơn đau thắt ngực ổn định trên nền tăng huyết áp độ 1.',
    amendmentReason: null,
    diagnoses: [
      {
        id: 301,
        icd10CodeId: 1020,
        code: 'I20.9',
        name: 'Cơn đau thắt ngực, không đặc hiệu',
        isPrimary: true,
        clinicalDiagnosis: 'Theo dõi thiếu máu cơ tim cục bộ mạn tính',
        status: 'PRELIMINARY',
        confirmedAt: null,
        recordedAt: '2026-11-07T08:08:00',
      },
      {
        id: 302,
        icd10CodeId: 1010,
        code: 'I10',
        name: 'Bệnh tăng huyết áp vô căn (nguyên phát)',
        isPrimary: false,
        clinicalDiagnosis: 'Tăng huyết áp đang điều trị ngoại trú',
        status: 'CONFIRMED',
        confirmedAt: '2026-11-07T08:08:00',
        recordedAt: '2026-11-07T08:08:00',
      },
    ],
    diagnosticSummary: READY_DIAGNOSTIC_SUMMARY,
    prescriptionSummary: {
      totalPrescriptions: 0,
      latestPrescriptionCode: null,
      latestPrescriptionStatus: null,
      latestIssuedAt: null,
      itemCount: 0,
    },
  },
  allowedActions: buildAllowedActions('IN_PROGRESS', true, false, READY_DIAGNOSTIC_SUMMARY),
};

export const CONSULTATION_WORKSPACE_CALLED_MOCK: ConsultationWorkspaceResponseDto = {
  actionOutcome: 'VIEWED',
  workspaceState: 'CALLED',
  canEditClinical: false,
  readOnlyReason:
    'Bệnh nhân đang ở trạng thái chờ/đã gọi. Vui lòng bấm Bắt đầu khám (IN_PROGRESS) để ghi nhận lâm sàng.',
  generatedAtUtc: '2026-11-07T02:16:00Z',
  visit: {
    ...COMMON_HOSPITAL,
    visitId: 213,
    visitCode: 'VIS-2026-0213',
    status: 'CALLED',
    statusLabel: 'Đã gọi vào phòng',
    visitType: 'SCHEDULED',
    visitTypeLabel: 'Đặt lịch hẹn',
    reasonForVisit: 'Ho khan kéo dài 2 tuần kèm sốt nhẹ về chiều',
    appointmentId: 502,
    appointmentCode: 'APT-20261107-0213',
    scheduledStartAt: '2026-11-07T08:15:00',
    scheduledEndAt: '2026-11-07T08:45:00',
    checkedInAt: '2026-11-07T08:05:00',
    startedAt: null,
    completedAt: null,
  },
  patient: {
    patientId: 102,
    patientCode: 'BN-2026-0213',
    fullName: 'Trần Thị Bích',
    dateOfBirth: '1992-09-20',
    birthYear: 1992,
    age: 34,
    gender: 'FEMALE',
    genderLabel: 'Nữ',
    phone: '0912345678',
    email: 'tranthibich@example.com',
    address: '45 Lê Lợi, Quận 1, TP. Hồ Chí Minh',
    bloodType: 'A+',
    allergiesSummary: 'Không ghi nhận tiền sử dị ứng thuốc',
  },
  queue: {
    queueEntryId: 13,
    formattedQueueEntryId: 'q-013',
    ticketNo: 'A013',
    queueNumber: 13,
    queueDate: '2026-11-07',
    queueType: 'EXAMINATION',
    status: 'CALLED',
    queueGroup: 'CALLED',
    ineligibleReason: null,
    priority: 'NORMAL',
    presenceStatus: 'PRESENT',
    arrivalClass: 'ON_TIME_OR_GRACE',
    servingOrder: 2,
    isEligible: false,
    isAppointmentProtected: true,
    holdReason: null,
    blockedReason: null,
    stateVersion: 2,
    servicePointId: 'sp-room-102',
    servicePointName: 'Phòng khám Nội tổng quát 102',
    scheduledAt: '08:15',
    arrivedAt: '08:05',
    readyAt: '08:06',
    calledAt: '08:16',
    startedAt: null,
    completedAt: null,
  },
  clinicalContext: {
    medicalRecordId: null,
    medicalRecordStatus: 'NOT_CREATED',
    finalizedAt: null,
    currentRevisionNo: null,
    isConfirmed: false,
    lastRecordedAt: null,
    presentingComplaint: 'Ho khan kéo dài 2 tuần kèm sốt nhẹ về chiều',
    symptomHistory: null,
    relevantHistoryAllergies: null,
    vitalSigns: {
      bpSystolicMmhg: 118,
      bpDiastolicMmhg: 76,
      bloodPressureLabel: '118/76 mmHg',
      pulseBpm: 78,
      temperatureC: 37.6,
      spo2Percent: 98,
      heightCm: 158,
      weightKg: 52,
      bmi: 20.8,
    },
    clinicalFindings: null,
    clinicalConclusion: null,
    amendmentReason: null,
    diagnoses: [],
    diagnosticSummary: READY_DIAGNOSTIC_SUMMARY,
    prescriptionSummary: {
      totalPrescriptions: 0,
      latestPrescriptionCode: null,
      latestPrescriptionStatus: null,
      latestIssuedAt: null,
      itemCount: 0,
    },
  },
  allowedActions: buildAllowedActions('CALLED', false, false, READY_DIAGNOSTIC_SUMMARY),
};

export const CONSULTATION_WORKSPACE_WAITING_MOCK: ConsultationWorkspaceResponseDto = {
  actionOutcome: 'VIEWED',
  workspaceState: 'WAITING',
  canEditClinical: false,
  readOnlyReason:
    'Bệnh nhân đang ở trạng thái chờ/đã gọi. Vui lòng bấm Bắt đầu khám (IN_PROGRESS) để ghi nhận lâm sàng.',
  generatedAtUtc: '2026-11-07T02:18:00Z',
  visit: {
    ...COMMON_HOSPITAL,
    visitId: 215,
    visitCode: 'VIS-2026-0215',
    status: 'WAITING',
    statusLabel: 'Đang chờ khám',
    visitType: 'WALK_IN',
    visitTypeLabel: 'Khám trực tiếp (Vãng lai)',
    reasonForVisit: 'Tái khám tăng huyết áp, chóng mặt khi thay đổi tư thế',
    appointmentId: null,
    appointmentCode: null,
    scheduledStartAt: null,
    scheduledEndAt: null,
    checkedInAt: '2026-11-07T08:12:00',
    startedAt: null,
    completedAt: null,
  },
  patient: {
    patientId: 105,
    patientCode: 'BN-2026-0215',
    fullName: 'Phạm Văn Lộc',
    dateOfBirth: '1954-02-10',
    birthYear: 1954,
    age: 72,
    gender: 'MALE',
    genderLabel: 'Nam',
    phone: '0938112233',
    email: null,
    address: '88 Điện Biên Phủ, Quận Bình Thạnh, TP. Hồ Chí Minh',
    bloodType: 'B+',
    allergiesSummary: 'Dị ứng hải sản',
  },
  queue: {
    queueEntryId: 15,
    formattedQueueEntryId: 'q-015',
    ticketNo: 'A015',
    queueNumber: 15,
    queueDate: '2026-11-07',
    queueType: 'EXAMINATION',
    status: 'READY',
    queueGroup: 'ELIGIBLE',
    ineligibleReason: null,
    priority: 'ELDERLY',
    presenceStatus: 'PRESENT',
    arrivalClass: 'WALK_IN',
    servingOrder: 3,
    isEligible: true,
    isAppointmentProtected: false,
    holdReason: null,
    blockedReason: null,
    stateVersion: 1,
    servicePointId: 'sp-room-102',
    servicePointName: 'Phòng khám Nội tổng quát 102',
    scheduledAt: null,
    arrivedAt: '08:12',
    readyAt: '08:12',
    calledAt: null,
    startedAt: null,
    completedAt: null,
  },
  clinicalContext: {
    medicalRecordId: null,
    medicalRecordStatus: 'NOT_CREATED',
    finalizedAt: null,
    currentRevisionNo: null,
    isConfirmed: false,
    lastRecordedAt: null,
    presentingComplaint: 'Tái khám tăng huyết áp, chóng mặt khi thay đổi tư thế',
    symptomHistory: null,
    relevantHistoryAllergies: null,
    vitalSigns: {
      bpSystolicMmhg: 152,
      bpDiastolicMmhg: 92,
      bloodPressureLabel: '152/92 mmHg',
      pulseBpm: 80,
      temperatureC: 36.7,
      spo2Percent: 96,
      heightCm: 165,
      weightKg: 64,
      bmi: 23.5,
    },
    clinicalFindings: null,
    clinicalConclusion: null,
    amendmentReason: null,
    diagnoses: [],
    diagnosticSummary: READY_DIAGNOSTIC_SUMMARY,
    prescriptionSummary: {
      totalPrescriptions: 0,
      latestPrescriptionCode: null,
      latestPrescriptionStatus: null,
      latestIssuedAt: null,
      itemCount: 0,
    },
  },
  allowedActions: buildAllowedActions('WAITING', false, false, READY_DIAGNOSTIC_SUMMARY),
};

export const CONSULTATION_WORKSPACE_AWAITING_RESULTS_MOCK: ConsultationWorkspaceResponseDto = {
  actionOutcome: 'VIEWED',
  workspaceState: 'AWAITING_RESULTS',
  canEditClinical: false,
  readOnlyReason:
    'Bệnh nhân đang chờ kết quả cận lâm sàng. Vui lòng gọi bệnh nhân vào khám lại sau khi có kết quả.',
  generatedAtUtc: '2026-11-07T02:20:00Z',
  visit: {
    ...COMMON_HOSPITAL,
    visitId: 217,
    visitCode: 'VIS-2026-0217',
    status: 'AWAITING_RESULTS',
    statusLabel: 'Chờ kết quả cận lâm sàng',
    visitType: 'SCHEDULED',
    visitTypeLabel: 'Đặt lịch hẹn',
    reasonForVisit: 'Đau thượng vị sau ăn, ợ chua nhiều về đêm',
    appointmentId: 505,
    appointmentCode: 'APT-20261107-0217',
    scheduledStartAt: '2026-11-07T07:45:00',
    scheduledEndAt: '2026-11-07T08:15:00',
    checkedInAt: '2026-11-07T07:40:00',
    startedAt: '2026-11-07T07:50:00',
    completedAt: null,
  },
  patient: {
    patientId: 107,
    patientCode: 'BN-2026-0217',
    fullName: 'Đỗ Minh Tuấn',
    dateOfBirth: '1981-07-12',
    birthYear: 1981,
    age: 45,
    gender: 'MALE',
    genderLabel: 'Nam',
    phone: '0977889900',
    email: null,
    address: '19 Võ Văn Tần, Quận 3, TP. Hồ Chí Minh',
    bloodType: 'AB+',
    allergiesSummary: 'Không ghi nhận dị ứng',
  },
  queue: {
    queueEntryId: 17,
    formattedQueueEntryId: 'q-017',
    ticketNo: 'A017',
    queueNumber: 17,
    queueDate: '2026-11-07',
    queueType: 'RETURN_CONSULTATION',
    status: 'WAITING',
    queueGroup: 'NOT_ELIGIBLE',
    ineligibleReason: 'AWAITING_RESULTS',
    priority: 'NORMAL',
    presenceStatus: 'PRESENT',
    arrivalClass: 'ON_TIME_OR_GRACE',
    servingOrder: 6,
    isEligible: false,
    isAppointmentProtected: false,
    holdReason: null,
    blockedReason: 'Chờ kết quả xét nghiệm máu và siêu âm bụng',
    stateVersion: 4,
    servicePointId: 'sp-room-102',
    servicePointName: 'Phòng khám Nội tổng quát 102',
    scheduledAt: '07:45',
    arrivedAt: '07:40',
    readyAt: '07:42',
    calledAt: '07:48',
    startedAt: '07:50',
    completedAt: null,
  },
  clinicalContext: {
    medicalRecordId: 803,
    medicalRecordStatus: 'DRAFT',
    finalizedAt: null,
    currentRevisionNo: 2,
    isConfirmed: false,
    lastRecordedAt: '2026-11-07T08:00:00',
    presentingComplaint: 'Đau thượng vị sau ăn, ợ chua nhiều về đêm',
    symptomHistory: 'Đau rát vùng thượng vị 1 tuần, tăng sau ăn đồ chua cay.',
    relevantHistoryAllergies: 'Không ghi nhận dị ứng',
    vitalSigns: {
      bpSystolicMmhg: 124,
      bpDiastolicMmhg: 80,
      bloodPressureLabel: '124/80 mmHg',
      pulseBpm: 74,
      temperatureC: 36.6,
      spo2Percent: 99,
      heightCm: 172,
      weightKg: 70,
      bmi: 23.7,
    },
    clinicalFindings: 'Ấn đau nhẹ vùng thượng vị, bụng mềm, gan lách không sờ chạm.',
    clinicalConclusion: 'Chờ kết quả siêu âm ổ bụng tổng quát và xét nghiệm máu.',
    amendmentReason: null,
    diagnoses: [
      {
        id: 305,
        icd10CodeId: 1120,
        code: 'K21.9',
        name: 'Bệnh trào ngược dạ dày - thực quản không có viêm thực quản',
        isPrimary: true,
        clinicalDiagnosis: 'Theo dõi trào ngược dạ dày thực quản - viêm dạ dày cấp',
        status: 'PRELIMINARY',
        confirmedAt: null,
        recordedAt: '2026-11-07T07:58:00',
      },
    ],
    diagnosticSummary: PENDING_DIAGNOSTIC_SUMMARY,
    prescriptionSummary: {
      totalPrescriptions: 0,
      latestPrescriptionCode: null,
      latestPrescriptionStatus: null,
      latestIssuedAt: null,
      itemCount: 0,
    },
  },
  allowedActions: buildAllowedActions('AWAITING_RESULTS', false, false, PENDING_DIAGNOSTIC_SUMMARY),
};

export const CONSULTATION_WORKSPACE_COMPLETED_MOCK: ConsultationWorkspaceResponseDto = {
  actionOutcome: 'VIEWED',
  workspaceState: 'COMPLETED',
  canEditClinical: false,
  readOnlyReason: 'Hồ sơ bệnh án đã được khóa hoàn tất (FINALIZED).',
  generatedAtUtc: '2026-11-07T02:25:00Z',
  visit: {
    ...COMMON_HOSPITAL,
    visitId: 209,
    visitCode: 'VIS-2026-0209',
    status: 'COMPLETED',
    statusLabel: 'Đã hoàn tất khám',
    visitType: 'SCHEDULED',
    visitTypeLabel: 'Đặt lịch hẹn',
    reasonForVisit: 'Khám nội tổng quát định kỳ, tư vấn chỉ số mỡ máu',
    appointmentId: 499,
    appointmentCode: 'APT-20261107-0209',
    scheduledStartAt: '2026-11-07T07:30:00',
    scheduledEndAt: '2026-11-07T08:00:00',
    checkedInAt: '2026-11-07T07:20:00',
    startedAt: '2026-11-07T07:32:00',
    completedAt: '2026-11-07T07:55:00',
  },
  patient: {
    patientId: 99,
    patientCode: 'BN-2026-0209',
    fullName: 'Ngô Thị Tuyết',
    dateOfBirth: '1997-11-03',
    birthYear: 1997,
    age: 29,
    gender: 'FEMALE',
    genderLabel: 'Nữ',
    phone: '0988776655',
    email: 'ngothituyet@example.com',
    address: '12 Pasteur, Quận 1, TP. Hồ Chí Minh',
    bloodType: 'O+',
    allergiesSummary: 'Không ghi nhận dị ứng',
  },
  queue: {
    queueEntryId: 9,
    formattedQueueEntryId: 'q-009',
    ticketNo: 'A009',
    queueNumber: 9,
    queueDate: '2026-11-07',
    queueType: 'EXAMINATION',
    status: 'COMPLETED',
    queueGroup: 'COMPLETED',
    ineligibleReason: null,
    priority: 'NORMAL',
    presenceStatus: 'PRESENT',
    arrivalClass: 'ON_TIME_OR_GRACE',
    servingOrder: 9,
    isEligible: false,
    isAppointmentProtected: true,
    holdReason: null,
    blockedReason: null,
    stateVersion: 5,
    servicePointId: 'sp-room-102',
    servicePointName: 'Phòng khám Nội tổng quát 102',
    scheduledAt: '07:30',
    arrivedAt: '07:20',
    readyAt: '07:22',
    calledAt: '07:30',
    startedAt: '07:32',
    completedAt: '07:55',
  },
  clinicalContext: {
    medicalRecordId: 799,
    medicalRecordStatus: 'FINALIZED',
    finalizedAt: '2026-11-07T07:55:00',
    currentRevisionNo: 2,
    isConfirmed: true,
    lastRecordedAt: '2026-11-07T07:54:00',
    presentingComplaint: 'Khám nội tổng quát định kỳ, tư vấn chỉ số mỡ máu',
    symptomHistory: 'Không đau ngực, không khó thở, sinh hoạt bình thường.',
    relevantHistoryAllergies: 'Không ghi nhận dị ứng',
    vitalSigns: {
      bpSystolicMmhg: 115,
      bpDiastolicMmhg: 75,
      bloodPressureLabel: '115/75 mmHg',
      pulseBpm: 72,
      temperatureC: 36.5,
      spo2Percent: 99,
      heightCm: 160,
      weightKg: 54,
      bmi: 21.1,
    },
    clinicalFindings: 'Tim đều, phổi trong, bụng mềm. Xét nghiệm lipid máu tăng nhẹ LDL-C.',
    clinicalConclusion:
      'Rối loạn chuyển hóa lipoprotein nhẹ, tư vấn chế độ ăn và kê đơn ngoại trú.',
    amendmentReason: null,
    diagnoses: [
      {
        id: 299,
        icd10CodeId: 1088,
        code: 'E78.5',
        name: 'Tăng lipid máu, không đặc hiệu',
        isPrimary: true,
        clinicalDiagnosis: 'Rối loạn lipid máu ngoại trú',
        status: 'CONFIRMED',
        confirmedAt: '2026-11-07T07:54:00',
        recordedAt: '2026-11-07T07:50:00',
      },
    ],
    diagnosticSummary: COMPLETED_DIAGNOSTIC_SUMMARY,
    prescriptionSummary: {
      totalPrescriptions: 1,
      latestPrescriptionCode: 'RX-20261107-009',
      latestPrescriptionStatus: 'ISSUED',
      latestIssuedAt: '2026-11-07T07:54:00',
      itemCount: 2,
    },
  },
  allowedActions: buildAllowedActions('COMPLETED', false, true, COMPLETED_DIAGNOSTIC_SUMMARY),
};

export const CONSULTATION_QUICK_SWITCH_ITEMS = [
  {
    visitCode: 'VIS-2026-0212',
    ticketNo: 'A012',
    patientName: 'Nguyễn Văn An',
    stateLabel: 'Đang khám (IN_PROGRESS)',
  },
  {
    visitCode: 'VIS-2026-0213',
    ticketNo: 'A013',
    patientName: 'Trần Thị Bích',
    stateLabel: 'Đã gọi vào (CALLED)',
  },
  {
    visitCode: 'VIS-2026-0215',
    ticketNo: 'A015',
    patientName: 'Phạm Văn Lộc',
    stateLabel: 'Chờ gọi (WAITING)',
  },
  {
    visitCode: 'VIS-2026-0217',
    ticketNo: 'A017',
    patientName: 'Đỗ Minh Tuấn',
    stateLabel: 'Chờ KQ CLS (AWAITING_RESULTS)',
  },
  {
    visitCode: 'VIS-2026-0209',
    ticketNo: 'A009',
    patientName: 'Ngô Thị Tuyết',
    stateLabel: 'Hoàn tất (COMPLETED)',
  },
] as const;

export function resolveMockConsultationWorkspace(
  idOrCode: string,
): ConsultationWorkspaceResponseDto {
  const key = idOrCode.trim().toUpperCase();

  if (
    key === 'VIS-2026-0213' ||
    key === '213' ||
    key === 'Q-013' ||
    key === '13' ||
    key === 'DQ-013' ||
    key === 'DQ-2' ||
    key === 'A013' ||
    key === 'B009'
  ) {
    return structuredClone(CONSULTATION_WORKSPACE_CALLED_MOCK);
  }

  if (
    key === 'VIS-2026-0215' ||
    key === '215' ||
    key === 'Q-015' ||
    key === '15' ||
    key === 'DQ-015' ||
    key === 'DQ-3' ||
    key === 'DQ-4' ||
    key === 'A015' ||
    key === 'B005' ||
    key === 'B010'
  ) {
    return structuredClone(CONSULTATION_WORKSPACE_WAITING_MOCK);
  }

  if (
    key === 'VIS-2026-0217' ||
    key === '217' ||
    key === 'Q-017' ||
    key === '17' ||
    key === 'DQ-017' ||
    key === 'DQ-5' ||
    key === 'A017' ||
    key === 'B006'
  ) {
    return structuredClone(CONSULTATION_WORKSPACE_AWAITING_RESULTS_MOCK);
  }

  if (
    key === 'VIS-2026-0209' ||
    key === '209' ||
    key === 'Q-009' ||
    key === '9' ||
    key === 'DQ-009' ||
    key === 'DQ-6' ||
    key === 'A009' ||
    key === 'B004'
  ) {
    return structuredClone(CONSULTATION_WORKSPACE_COMPLETED_MOCK);
  }

  return structuredClone(CONSULTATION_WORKSPACE_IN_PROGRESS_MOCK);
}

export function openMockConsultationWorkspace(
  idOrCode: string,
  request?: OpenConsultationWorkspaceRequest,
): ConsultationWorkspaceResponseDto {
  const base = resolveMockConsultationWorkspace(idOrCode);
  const isAlreadyInProgress = base.workspaceState === 'IN_PROGRESS';
  const currentStateVersion = base.queue?.stateVersion ?? 1;

  return {
    ...base,
    actionOutcome: isAlreadyInProgress ? 'ALREADY_IN_PROGRESS' : 'OPENED',
    workspaceState: 'IN_PROGRESS',
    canEditClinical: true,
    readOnlyReason: null,
    generatedAtUtc: '2026-11-07T02:30:00Z',
    visit: {
      ...base.visit,
      status: 'IN_PROGRESS',
      statusLabel: 'Đang khám lâm sàng',
      startedAt: base.visit.startedAt ?? '2026-11-07T08:17:00',
    },
    queue: base.queue
      ? {
          ...base.queue,
          status: 'IN_PROGRESS',
          queueGroup: 'IN_PROGRESS',
          presenceStatus: 'PRESENT',
          startedAt: base.queue.startedAt ?? '08:17',
          stateVersion: isAlreadyInProgress ? currentStateVersion : currentStateVersion + 1,
        }
      : null,
    clinicalContext: {
      ...base.clinicalContext,
      medicalRecordId: base.clinicalContext.medicalRecordId ?? 810,
      medicalRecordStatus: 'DRAFT',
      currentRevisionNo: base.clinicalContext.currentRevisionNo ?? 1,
      lastRecordedAt: base.clinicalContext.lastRecordedAt ?? '2026-11-07T08:17:00',
      presentingComplaint:
        base.clinicalContext.presentingComplaint ??
        base.visit.reasonForVisit ??
        (request?.notes ? request.notes.trim() : null),
    },
    allowedActions: buildAllowedActions(
      'IN_PROGRESS',
      true,
      false,
      base.clinicalContext.diagnosticSummary,
    ),
  };
}

export function recordMockClinicalExamination(
  idOrCode: string,
  request: RecordClinicalExaminationRequest,
): ConsultationWorkspaceResponseDto {
  const base = resolveMockConsultationWorkspace(idOrCode);
  const currentRevisionNo = base.clinicalContext.currentRevisionNo ?? 1;
  const nextRevisionNo = currentRevisionNo + 1;

  const v = request.vitalSigns;
  const bpSystolic =
    v?.bpSystolicMmhg ?? request.bpSystolicMmhg ?? base.clinicalContext.vitalSigns.bpSystolicMmhg;
  const bpDiastolic =
    v?.bpDiastolicMmhg ??
    request.bpDiastolicMmhg ??
    base.clinicalContext.vitalSigns.bpDiastolicMmhg;
  const pulseBpm = v?.pulseBpm ?? request.pulseBpm ?? base.clinicalContext.vitalSigns.pulseBpm;
  const temperatureC =
    v?.temperatureC ?? request.temperatureC ?? base.clinicalContext.vitalSigns.temperatureC;
  const spo2Percent =
    v?.spo2Percent ?? request.spo2Percent ?? base.clinicalContext.vitalSigns.spo2Percent;
  const heightCm = v?.heightCm ?? request.heightCm ?? base.clinicalContext.vitalSigns.heightCm;
  const weightKg = v?.weightKg ?? request.weightKg ?? base.clinicalContext.vitalSigns.weightKg;

  const presentar =
    request.presentingComplaint !== undefined && request.presentingComplaint !== null
      ? request.presentingComplaint.trim() || base.clinicalContext.presentingComplaint
      : base.clinicalContext.presentingComplaint;
  const symptomHistory =
    request.symptomHistory !== undefined && request.symptomHistory !== null
      ? request.symptomHistory.trim() || base.clinicalContext.symptomHistory
      : base.clinicalContext.symptomHistory;
  const relevantHistoryAllergies =
    request.relevantHistoryAllergies !== undefined && request.relevantHistoryAllergies !== null
      ? request.relevantHistoryAllergies.trim() || base.clinicalContext.relevantHistoryAllergies
      : base.clinicalContext.relevantHistoryAllergies;
  const clinicalFindings =
    request.clinicalFindings !== undefined && request.clinicalFindings !== null
      ? request.clinicalFindings.trim() || base.clinicalContext.clinicalFindings
      : base.clinicalContext.clinicalFindings;
  const clinicalConclusion =
    request.clinicalConclusion !== undefined && request.clinicalConclusion !== null
      ? request.clinicalConclusion.trim() || base.clinicalContext.clinicalConclusion
      : base.clinicalContext.clinicalConclusion;
  const amendmentReason =
    request.amendmentReason !== undefined && request.amendmentReason !== null
      ? request.amendmentReason.trim() || null
      : null;

  return {
    ...base,
    actionOutcome: 'CLINICAL_EXAMINATION_RECORDED',
    workspaceState: 'IN_PROGRESS',
    canEditClinical: true,
    readOnlyReason: null,
    generatedAtUtc: '2026-11-07T02:35:00Z',
    visit: {
      ...base.visit,
      status: 'IN_PROGRESS',
      statusLabel: 'Đang khám lâm sàng',
    },
    patient: {
      ...base.patient,
      allergiesSummary: relevantHistoryAllergies ?? base.patient.allergiesSummary,
    },
    clinicalContext: {
      ...base.clinicalContext,
      medicalRecordId: base.clinicalContext.medicalRecordId ?? 801,
      medicalRecordStatus: 'DRAFT',
      currentRevisionNo: nextRevisionNo,
      isConfirmed: request.isConfirmed ?? true,
      lastRecordedAt: '2026-11-07T08:15:00',
      presentingComplaint: presentar,
      symptomHistory,
      relevantHistoryAllergies,
      vitalSigns: {
        bpSystolicMmhg: bpSystolic,
        bpDiastolicMmhg: bpDiastolic,
        bloodPressureLabel: formatBloodPressureLabel(bpSystolic, bpDiastolic),
        pulseBpm,
        temperatureC:
          temperatureC !== null && temperatureC !== undefined
            ? Math.round(temperatureC * 10) / 10
            : null,
        spo2Percent,
        heightCm:
          heightCm !== null && heightCm !== undefined ? Math.round(heightCm * 10) / 10 : null,
        weightKg:
          weightKg !== null && weightKg !== undefined ? Math.round(weightKg * 10) / 10 : null,
        bmi: calculateClinicalBmi(heightCm, weightKg),
      },
      clinicalFindings,
      clinicalConclusion,
      amendmentReason,
    },
    allowedActions: buildAllowedActions(
      'IN_PROGRESS',
      true,
      false,
      base.clinicalContext.diagnosticSummary,
    ),
  };
}
