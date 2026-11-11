import type {
  DiagnosticOrderClinicalSummaryDto,
  DiagnosticServicePointOptionDto,
  DiagnosticStaffProfile,
} from '../types/diagnostic-order-review.types';
import type {
  DiagnosticServiceExecutionDetailResponseDto,
  DiagnosticServiceExecutionEligibilityDto,
  DiagnosticServiceExecutionHistoryItemDto,
  DiagnosticServiceExecutionItemDto,
  DiagnosticServiceExecutionListResponseDto,
  DiagnosticServiceExecutionSummaryStatsDto,
  GetDiagnosticExecutionsParams,
  UpdateDiagnosticServiceStatusBatchRequest,
  UpdateDiagnosticServiceStatusRequest,
  UpdateDiagnosticServiceStatusResponseDto,
} from '../types/diagnostic-execution.types';
import {
  DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK,
  DIAGNOSTIC_SERVICE_POINTS_MOCK,
} from './diagnostic-order-review-mock';

export const DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK: DiagnosticStaffProfile = {
  ...DIAGNOSTIC_REVIEW_STAFF_PROFILE_MOCK,
};

export const DIAGNOSTIC_EXECUTION_SERVICE_POINTS_MOCK: DiagnosticServicePointOptionDto[] =
  DIAGNOSTIC_SERVICE_POINTS_MOCK.map((sp) => ({ ...sp }));

// Nhãn trạng thái phiếu chỉ định / hàng đợi hiển thị ở màn thực hiện kỹ thuật.
const ORDER_STATUS_LABELS: Record<string, string> = {
  ORDERED: 'Chờ xác nhận',
  CONFIRMED: 'Đã xác nhận',
  IN_PROGRESS: 'Đang thực hiện',
  COMPLETED: 'Đã hoàn tất',
  CANCELLED: 'Đã hủy',
};

const QUEUE_STATUS_LABELS: Record<string, string> = {
  WAITING: 'Chờ tiếp nhận',
  READY: 'Sẵn sàng thực hiện',
  CALLED: 'Đang gọi vào phòng',
  IN_PROGRESS: 'Đang thực hiện',
  COMPLETED: 'Đã hoàn tất',
  SKIPPED: 'Qua lượt',
  CANCELLED: 'Đã hủy',
  RESERVED: 'Đã giữ chỗ',
};

const CATEGORY_LABELS: Record<string, string> = {
  LABORATORY: 'Xét nghiệm',
  IMAGING: 'Chẩn đoán hình ảnh',
  FUNCTIONAL: 'Thăm dò chức năng',
};

const PRIORITY_LABELS: Record<string, string> = {
  NORMAL: 'Thường',
  URGENT: 'Ưu tiên',
  STAT: 'Cấp tốc',
};

interface ExecutionSeed {
  orderId: number;
  orderCode: string;
  visitId: number;
  visitCode: string;
  patientCode: string;
  patientName: string;
  patientDob: string;
  patientAge: number;
  patientGender: string;
  allergies?: string;
  serviceCode: string;
  serviceName: string;
  category: 'LABORATORY' | 'IMAGING' | 'FUNCTIONAL';
  priority: 'NORMAL' | 'URGENT' | 'STAT';
  status: 'ORDERED' | 'CONFIRMED' | 'IN_PROGRESS' | 'COMPLETED' | 'CANCELLED';
  queueStatus:
    'WAITING' | 'READY' | 'CALLED' | 'IN_PROGRESS' | 'COMPLETED' | 'SKIPPED' | 'CANCELLED';
  ticketNo: string;
  queueNumber: number;
  roomId: number;
  roomCode: string;
  roomName: string;
  servicePointId: number;
  servicePointName: string;
  requiresResultVerification?: boolean;
  requiredForConclusion?: boolean;
  stateVersion: number;
  orderedAt: string;
  confirmedAt?: string;
  startedAt?: string;
  completedAt?: string;
  specimenCode?: string;
  specimenType?: string;
  equipmentCode?: string;
  executionNote?: string;
  lastTransitionReason?: string;
  unitPrice: number;
  doctorName: string;
  instruction?: string;
}

function buildEligibility(seed: ExecutionSeed): DiagnosticServiceExecutionEligibilityDto {
  const isConfirmed = seed.status === 'CONFIRMED';
  const isInProgress = seed.status === 'IN_PROGRESS';
  const isQueueEligible = seed.queueStatus !== 'SKIPPED' && seed.queueStatus !== 'CANCELLED';
  const canUpdateStatus = (isConfirmed || isInProgress) && isQueueEligible;
  const canStartExecution = isConfirmed && isQueueEligible;
  const canCompleteExecution = isInProgress;
  const canPauseExecution = isInProgress;
  const canCancelExecution = isConfirmed || isInProgress;

  let disabledReason: string | null = null;
  if (seed.status === 'ORDERED') {
    disabledReason =
      'Phiếu chỉ định chưa được xác nhận (UC39). Cần xác nhận trước khi thực hiện kỹ thuật.';
  } else if (seed.status === 'COMPLETED' || seed.status === 'CANCELLED') {
    disabledReason = `Phiếu chỉ định đã ở trạng thái kết thúc ('${seed.status}'), không thể cập nhật.`;
  } else if (!isQueueEligible) {
    disabledReason =
      'Lượt hàng đợi đang ở trạng thái SKIPPED — cần khôi phục lượt trước khi bắt đầu thực hiện.';
  }

  const allowedTargetStatuses: string[] = [];
  if (isConfirmed && isQueueEligible) {
    allowedTargetStatuses.push('IN_PROGRESS', 'CANCELLED');
  } else if (isInProgress) {
    allowedTargetStatuses.push('COMPLETED', 'CONFIRMED', 'READY', 'WAITING', 'CANCELLED');
  }

  return {
    isEligible: canUpdateStatus,
    canUpdateStatus,
    canStartExecution,
    canCompleteExecution,
    canPauseExecution,
    canCancelExecution,
    disabledReason,
    allowedTargetStatuses,
    isOrderConfirmedOrInProgress: isConfirmed || isInProgress,
    isQueueEligible,
    isVisitActive: true,
    isPatientActive: true,
    isServiceActive: true,
    isDepartmentActive: true,
    isServicePointActive: true,
    isRoomActive: true,
    isResultNotFinalized: true,
    hasExecutionPermission: true,
  };
}

function buildItem(seed: ExecutionSeed): DiagnosticServiceExecutionItemDto {
  const eligibility = buildEligibility(seed);
  return {
    orderId: seed.orderId,
    orderCode: seed.orderCode,
    visitId: seed.visitId,
    visitCode: seed.visitCode,
    visitStatus: seed.status === 'COMPLETED' ? 'AWAITING_RESULTS' : 'WAITING_DIAGNOSTIC',
    patient: {
      patientId: seed.visitId * 10,
      patientCode: seed.patientCode,
      fullName: seed.patientName,
      dateOfBirth: seed.patientDob,
      age: seed.patientAge,
      gender: seed.patientGender,
      phone: '0908' + String(seed.orderId).padStart(6, '0'),
      bloodType: 'O+',
      allergies: seed.allergies ?? null,
    },
    orderingDoctorId: 100 + seed.orderId,
    orderingDoctorCode: `DOC-${100 + seed.orderId}`,
    orderingDoctorName: seed.doctorName,
    serviceId: 500 + seed.orderId,
    serviceCode: seed.serviceCode,
    serviceName: seed.serviceName,
    category: seed.category,
    categoryLabel: CATEGORY_LABELS[seed.category] ?? seed.category,
    departmentId: 12,
    departmentCode: 'KHOA-CLS',
    departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
    specialtyId: 4,
    specialtyCode: 'NOI-TONG',
    specialtyName: 'Nội tổng quát',
    unitPrice: seed.unitPrice,
    requiresResultVerification: seed.requiresResultVerification ?? false,
    priority: seed.priority,
    priorityLabel: PRIORITY_LABELS[seed.priority] ?? seed.priority,
    status: seed.status,
    statusLabel: ORDER_STATUS_LABELS[seed.status] ?? seed.status,
    requiredForConclusion: seed.requiredForConclusion ?? false,
    instruction: seed.instruction ?? null,
    orderedAt: seed.orderedAt,
    confirmedAt: seed.confirmedAt ?? null,
    confirmedByStaffId: seed.confirmedAt ? 304 : null,
    confirmedByStaffCode: seed.confirmedAt ? 'DIAG-009' : null,
    confirmedByStaffName: seed.confirmedAt ? 'CN. Lê Hoàng Minh' : null,
    confirmationNote: seed.confirmedAt ? 'Đã đối chiếu chỉ định và điều kiện chuẩn bị.' : null,
    receivedAt: seed.confirmedAt ?? null,
    receivedByStaffId: seed.confirmedAt ? 304 : null,
    receivedByStaffCode: seed.confirmedAt ? 'DIAG-009' : null,
    receivedByStaffName: seed.confirmedAt ? 'CN. Lê Hoàng Minh' : null,
    receptionNote: null,
    startedAt: seed.startedAt ?? null,
    startedByStaffId: seed.startedAt ? 305 : null,
    startedByStaffCode: seed.startedAt ? 'DIAG-010' : null,
    startedByStaffName: seed.startedAt ? 'CN. Trần Ngọc Phúc' : null,
    completedAt: seed.completedAt ?? null,
    completedByStaffId: seed.completedAt ? 305 : null,
    completedByStaffCode: seed.completedAt ? 'DIAG-010' : null,
    completedByStaffName: seed.completedAt ? 'CN. Trần Ngọc Phúc' : null,
    specimenCode: seed.specimenCode ?? null,
    specimenType: seed.specimenType ?? null,
    equipmentCode: seed.equipmentCode ?? null,
    executionNote: seed.executionNote ?? null,
    lastTransitionReason: seed.lastTransitionReason ?? null,
    queueEntryId: 9000 + seed.orderId,
    formattedQueueEntryId: `QE-${String(9000 + seed.orderId).padStart(8, '0')}`,
    ticketNo: seed.ticketNo,
    queueNumber: seed.queueNumber,
    queueDate: '2026-11-11',
    queueStatus: seed.queueStatus,
    queueStatusLabel: QUEUE_STATUS_LABELS[seed.queueStatus] ?? seed.queueStatus,
    presenceStatus:
      seed.queueStatus === 'SKIPPED' || seed.queueStatus === 'CANCELLED' ? 'LEFT' : 'PRESENT',
    manualPriority: 'NORMAL',
    effectiveClinicalPriority: 'NORMAL',
    arrivedAt: seed.confirmedAt ?? null,
    readyAt:
      seed.queueStatus === 'READY' || seed.queueStatus === 'CALLED' || seed.status === 'IN_PROGRESS'
        ? (seed.confirmedAt ?? null)
        : null,
    calledAt:
      seed.status === 'IN_PROGRESS' || seed.status === 'COMPLETED'
        ? (seed.startedAt ?? null)
        : null,
    stateVersion: seed.stateVersion,
    servicePointId: seed.servicePointId,
    formattedServicePointId: `SP-${seed.servicePointId}`,
    servicePointName: seed.servicePointName,
    roomId: seed.roomId,
    roomCode: seed.roomCode,
    roomName: seed.roomName,
    roomLocation: 'Tầng 2 - Khu B',
    hasResult: false,
    isResultFinalized: false,
    resultStatus: seed.status === 'COMPLETED' ? 'PENDING_ENTRY' : 'NOT_STARTED',
    isAdditionalOrder: false,
    orderSequenceNo: 1,
    followUpToOrderId: null,
    followUpToOrderCode: null,
    additionalOrderReason: null,
    canUpdateStatus: eligibility.canUpdateStatus,
    canStartExecution: eligibility.canStartExecution,
    canCompleteExecution: eligibility.canCompleteExecution,
    canPauseExecution: eligibility.canPauseExecution,
    canCancelExecution: eligibility.canCancelExecution,
    canEnterResult: seed.status === 'COMPLETED',
    disabledReason: eligibility.disabledReason,
    allowedTargetStatuses: eligibility.allowedTargetStatuses,
    eligibility,
  };
}

const EXECUTION_SEEDS: ExecutionSeed[] = [
  {
    orderId: 61,
    orderCode: 'DIA-202611-0061',
    visitId: 1101,
    visitCode: 'VIS-202611-1101',
    patientCode: 'PAT-3011',
    patientName: 'Nguyễn Văn An',
    patientDob: '1980-05-12',
    patientAge: 46,
    patientGender: 'MALE',
    serviceCode: 'XN-CTM-001',
    serviceName: 'Công thức máu toàn bộ (CBC)',
    category: 'LABORATORY',
    priority: 'URGENT',
    status: 'CONFIRMED',
    queueStatus: 'READY',
    ticketNo: 'C061',
    queueNumber: 61,
    roomId: 21,
    roomCode: 'P.XN-201',
    roomName: 'Phòng Xét nghiệm 201',
    servicePointId: 201,
    servicePointName: 'Điểm lấy mẫu & Sinh hóa huyết học 01',
    requiredForConclusion: true,
    stateVersion: 3,
    orderedAt: '2026-11-11T07:30:00Z',
    confirmedAt: '2026-11-11T07:40:00Z',
    unitPrice: 120000,
    doctorName: 'BS.CKI. Nguyễn Minh Khôi',
    instruction: 'Bệnh nhân đã nhịn ăn 8 giờ, lấy mẫu ưu tiên buổi sáng.',
  },
  {
    orderId: 62,
    orderCode: 'DIA-202611-0062',
    visitId: 1102,
    visitCode: 'VIS-202611-1102',
    patientCode: 'PAT-3012',
    patientName: 'Trần Thị Bích',
    patientDob: '1992-09-02',
    patientAge: 34,
    patientGender: 'FEMALE',
    allergies: 'Dị ứng Penicillin',
    serviceCode: 'CDHA-XQ-001',
    serviceName: 'X-Quang ngực thẳng PA',
    category: 'IMAGING',
    priority: 'NORMAL',
    status: 'CONFIRMED',
    queueStatus: 'SKIPPED',
    ticketNo: 'C062',
    queueNumber: 62,
    roomId: 31,
    roomCode: 'P.CDHA-301',
    roomName: 'Phòng X-Quang 301',
    servicePointId: 301,
    servicePointName: 'Điểm chụp X-Quang 01',
    stateVersion: 2,
    orderedAt: '2026-11-11T07:35:00Z',
    confirmedAt: '2026-11-11T07:45:00Z',
    unitPrice: 180000,
    doctorName: 'BS.CKI. Lê Thị Mai',
    instruction: 'Cần kiểm tra lại điều kiện thai kỳ trước khi chụp.',
  },
  {
    orderId: 63,
    orderCode: 'DIA-202611-0063',
    visitId: 1103,
    visitCode: 'VIS-202611-1103',
    patientCode: 'PAT-3013',
    patientName: 'Phạm Quốc Cường',
    patientDob: '1968-01-25',
    patientAge: 58,
    patientGender: 'MALE',
    serviceCode: 'TDCN-ECG-001',
    serviceName: 'Điện tâm đồ 12 chuyển đạo',
    category: 'FUNCTIONAL',
    priority: 'NORMAL',
    status: 'IN_PROGRESS',
    queueStatus: 'IN_PROGRESS',
    ticketNo: 'C063',
    queueNumber: 63,
    roomId: 41,
    roomCode: 'P.TDCN-401',
    roomName: 'Phòng Thăm dò chức năng 401',
    servicePointId: 401,
    servicePointName: 'Điểm thăm dò chức năng tim mạch',
    stateVersion: 5,
    orderedAt: '2026-11-11T07:20:00Z',
    confirmedAt: '2026-11-11T07:30:00Z',
    startedAt: '2026-11-11T08:05:00Z',
    unitPrice: 150000,
    doctorName: 'BS.CKI. Nguyễn Minh Khôi',
    executionNote: 'Đang đo ECG, bệnh nhân hợp tác tốt.',
  },
  {
    orderId: 64,
    orderCode: 'DIA-202611-0064',
    visitId: 1104,
    visitCode: 'VIS-202611-1104',
    patientCode: 'PAT-3014',
    patientName: 'Đỗ Hồng Đào',
    patientDob: '1955-11-30',
    patientAge: 70,
    patientGender: 'FEMALE',
    serviceCode: 'XN-SH-009',
    serviceName: 'Sinh hóa máu: Lipid panel + HbA1c',
    category: 'LABORATORY',
    priority: 'NORMAL',
    status: 'IN_PROGRESS',
    queueStatus: 'IN_PROGRESS',
    ticketNo: 'C064',
    queueNumber: 64,
    roomId: 21,
    roomCode: 'P.XN-201',
    roomName: 'Phòng Xét nghiệm 201',
    servicePointId: 201,
    servicePointName: 'Điểm lấy mẫu & Sinh hóa huyết học 01',
    requiresResultVerification: true,
    requiredForConclusion: true,
    stateVersion: 6,
    orderedAt: '2026-11-11T07:10:00Z',
    confirmedAt: '2026-11-11T07:22:00Z',
    startedAt: '2026-11-11T08:00:00Z',
    specimenCode: 'SPC-2026-0064',
    specimenType: 'Máu tĩnh mạch',
    unitPrice: 320000,
    doctorName: 'BS.CKII. Vũ Thanh Long',
    instruction: 'Kết quả bắt buộc trước khi bác sĩ kết luận (UC36).',
  },
  {
    orderId: 65,
    orderCode: 'DIA-202611-0065',
    visitId: 1105,
    visitCode: 'VIS-202611-1105',
    patientCode: 'PAT-3015',
    patientName: 'Hoàng Minh Tuấn',
    patientDob: '1988-03-14',
    patientAge: 38,
    patientGender: 'MALE',
    serviceCode: 'CDHA-US-010',
    serviceName: 'Siêu âm ổ bụng tổng quát',
    category: 'IMAGING',
    priority: 'NORMAL',
    status: 'COMPLETED',
    queueStatus: 'COMPLETED',
    ticketNo: 'C065',
    queueNumber: 65,
    roomId: 32,
    roomCode: 'P.CDHA-302',
    roomName: 'Phòng Siêu âm 302',
    servicePointId: 301,
    servicePointName: 'Điểm siêu âm ổ bụng 01',
    stateVersion: 8,
    orderedAt: '2026-11-11T06:50:00Z',
    confirmedAt: '2026-11-11T07:00:00Z',
    startedAt: '2026-11-11T07:30:00Z',
    completedAt: '2026-11-11T08:15:00Z',
    equipmentCode: 'US-ALOKA-02',
    unitPrice: 220000,
    doctorName: 'BS.CKI. Lê Thị Mai',
  },
  {
    orderId: 66,
    orderCode: 'DIA-202611-0066',
    visitId: 1106,
    visitCode: 'VIS-202611-1106',
    patientCode: 'PAT-3016',
    patientName: 'Võ Thị Lan',
    patientDob: '1975-07-08',
    patientAge: 51,
    patientGender: 'FEMALE',
    serviceCode: 'XN-VS-020',
    serviceName: 'Vi sinh: Cấy máu + Kháng sinh đồ',
    category: 'LABORATORY',
    priority: 'STAT',
    status: 'ORDERED',
    queueStatus: 'WAITING',
    ticketNo: 'C066',
    queueNumber: 66,
    roomId: 22,
    roomCode: 'P.XN-202',
    roomName: 'Phòng Vi sinh 202',
    servicePointId: 201,
    servicePointName: 'Điểm lấy mẫu & Sinh hóa huyết học 01',
    stateVersion: 1,
    orderedAt: '2026-11-11T08:20:00Z',
    unitPrice: 450000,
    doctorName: 'BS.CKII. Vũ Thanh Long',
  },
];

const INITIAL_EXECUTION_ITEMS: DiagnosticServiceExecutionItemDto[] = EXECUTION_SEEDS.map(buildItem);

const CLINICAL_CONTEXT_BY_VISIT_ID: Record<number, DiagnosticOrderClinicalSummaryDto> = {
  1101: {
    visitId: 1101,
    visitCode: 'VIS-202611-1101',
    visitStatus: 'WAITING_DIAGNOSTIC',
    reasonForVisit: 'Mệt mỏi, giảm cân 3kg trong 1 tháng, khát nước nhiều',
    presentingComplaint:
      'Tiền sử đái tháo đường type 2, đường huyết mao mạch sáng nay 12.4 mmol/L.',
    clinicalFindings: 'Tỉnh táo, niêm mạc hơi khô, mạch 88 lần/phút, HA 135/85 mmHg.',
    primaryDiagnosisCode: 'E11.65',
    primaryDiagnosisName: 'Đái tháo đường type 2 có tăng đường huyết chưa kiểm soát',
    clinicalDiagnosis: 'Theo dõi đợt mất bù đường huyết cấp trên nền ĐTĐ type 2',
  },
  1102: {
    visitId: 1102,
    visitCode: 'VIS-202611-1102',
    visitStatus: 'WAITING_DIAGNOSTIC',
    reasonForVisit: 'Ho khan kéo dài 2 tuần, tức ngực nhẹ',
    presentingComplaint: 'Ho nhiều về đêm, không sốt, không khạc ra máu.',
    clinicalFindings: 'Phổi ran nổ rải rác đáy phổi phải, nhịp tim đều.',
    primaryDiagnosisCode: 'J20.9',
    primaryDiagnosisName: 'Viêm phế quản cấp, không xác định tác nhân',
    clinicalDiagnosis: 'Nghi viêm phế quản cấp — cần X-Quang ngực loại trừ viêm phổi',
  },
  1103: {
    visitId: 1103,
    visitCode: 'VIS-202611-1103',
    visitStatus: 'WAITING_DIAGNOSTIC',
    reasonForVisit: 'Hồi hộp trống ngực khi gắng sức',
    presentingComplaint: 'Cơn hồi hộp 10-15 phút, 3 ngày nay, nghỉ ngơi đỡ.',
    clinicalFindings: 'Nhịp tim không đều từng lúc, HA 145/90 mmHg.',
    primaryDiagnosisCode: 'I49.3',
    primaryDiagnosisName: 'Ngoại tâm thu thất',
    clinicalDiagnosis: 'Ngoại tâm thu thất trên nền tăng huyết áp độ 2',
  },
  1104: {
    visitId: 1104,
    visitCode: 'VIS-202611-1104',
    visitStatus: 'WAITING_DIAGNOSTIC',
    reasonForVisit: 'Khám định kỳ đái tháo đường + mỡ máu',
    presentingComplaint: 'ĐTĐ type 2 10 năm, đang dùng Metformin 850mg x2/ngày.',
    clinicalFindings: 'BMI 27.1, vòng eo 96cm, HA 140/85 mmHg.',
    primaryDiagnosisCode: 'E78.5',
    primaryDiagnosisName: 'Rối loạn lipid máu không xác định',
    clinicalDiagnosis: 'ĐTĐ type 2 kèm rối loạn lipid — đánh giá kiểm soát đường huyết',
  },
  1105: {
    visitId: 1105,
    visitCode: 'VIS-202611-1105',
    visitStatus: 'AWAITING_RESULTS',
    reasonForVisit: 'Đau âm ỉ hạ sườn phải sau ăn dầu mỡ',
    presentingComplaint: 'Đau tức vùng hạ sườn phải lan ra sau lưng, đầy bụng khó tiêu.',
    clinicalFindings: 'Bụng mềm, ấn tức điểm túi mật, Murphy (-).',
    primaryDiagnosisCode: 'K80.2',
    primaryDiagnosisName: 'Sỏi túi mật không viêm túi mật',
    clinicalDiagnosis: 'Sỏi túi mật — siêu âm ổ bụng Doppler màu',
  },
  1106: {
    visitId: 1106,
    visitCode: 'VIS-202611-1106',
    visitStatus: 'WAITING_DIAGNOSTIC',
    reasonForVisit: 'Sốt cao 39.2 độ C kèm rét run 3 ngày',
    presentingComplaint: 'Sốt liên tục không đáp ứng hạ sốt thông thường.',
    clinicalFindings: 'Da nóng, mạch 112 lần/phút, HA 100/60 mmHg.',
    primaryDiagnosisCode: 'A41.9',
    primaryDiagnosisName: 'Nhiễm trùng huyết, không xác định tác nhân',
    clinicalDiagnosis: 'Nghi nhiễm trùng huyết — cấy máu khẩn trước kháng sinh',
  },
};

function cloneExecutionItems(
  items: DiagnosticServiceExecutionItemDto[],
): DiagnosticServiceExecutionItemDto[] {
  return items.map((item) => ({
    ...item,
    patient: { ...item.patient },
    eligibility: {
      ...item.eligibility,
      allowedTargetStatuses: [...item.eligibility.allowedTargetStatuses],
    },
    allowedTargetStatuses: [...item.allowedTargetStatuses],
  }));
}

let mockExecutionState: DiagnosticServiceExecutionItemDto[] =
  cloneExecutionItems(INITIAL_EXECUTION_ITEMS);

let mockExecutionHistory: DiagnosticServiceExecutionHistoryItemDto[] = [];
let mockAuditLogSeq = 100;

export const DIAGNOSTIC_EXECUTION_ITEMS_MOCK: DiagnosticServiceExecutionItemDto[] =
  cloneExecutionItems(INITIAL_EXECUTION_ITEMS);

export function resetDiagnosticExecutionMockState(): void {
  mockExecutionState = cloneExecutionItems(INITIAL_EXECUTION_ITEMS);
  mockExecutionHistory = [];
  mockAuditLogSeq = 100;
}

function computeExecutionSummary(
  items: DiagnosticServiceExecutionItemDto[],
): DiagnosticServiceExecutionSummaryStatsDto {
  let pendingConfirmationCount = 0;
  let waitingExecutionCount = 0;
  let readyToStartCount = 0;
  let inProgressCount = 0;
  let completedCount = 0;
  let cancelledCount = 0;
  let urgentOrStatCount = 0;
  let mandatoryCount = 0;
  let laboratoryCount = 0;
  let imagingCount = 0;
  let functionalCount = 0;

  for (const item of items) {
    const status = item.status.toUpperCase();
    if (status === 'ORDERED') {
      pendingConfirmationCount += 1;
    } else if (status === 'CONFIRMED') {
      waitingExecutionCount += 1;
      if (item.canStartExecution) readyToStartCount += 1;
    } else if (status === 'IN_PROGRESS') {
      inProgressCount += 1;
    } else if (status === 'COMPLETED') {
      completedCount += 1;
    } else if (status === 'CANCELLED') {
      cancelledCount += 1;
    }
    if (item.priority === 'URGENT' || item.priority === 'STAT') urgentOrStatCount += 1;
    if (item.requiredForConclusion) mandatoryCount += 1;
    if (item.category === 'LABORATORY') laboratoryCount += 1;
    else if (item.category === 'IMAGING') imagingCount += 1;
    else if (item.category === 'FUNCTIONAL') functionalCount += 1;
  }

  return {
    totalOrders: items.length,
    pendingConfirmationCount,
    waitingExecutionCount,
    readyToStartCount,
    inProgressCount,
    completedCount,
    cancelledCount,
    urgentOrStatCount,
    mandatoryCount,
    laboratoryCount,
    imagingCount,
    functionalCount,
  };
}

function findMockExecutionByTarget(
  idOrCode: string | number,
): DiagnosticServiceExecutionItemDto | undefined {
  const raw = String(idOrCode).trim();
  if (!raw) return undefined;
  const upper = raw.toUpperCase();
  const numericId = Number(raw);

  return mockExecutionState.find((item) => {
    if (!Number.isNaN(numericId) && item.orderId === numericId) return true;
    if (item.orderCode.toUpperCase() === upper) return true;
    if (item.ticketNo && item.ticketNo.toUpperCase() === upper) return true;
    if (item.visitCode.toUpperCase() === upper) return true;
    const tailDigits = raw.match(/(\d+)$/)?.[1];
    if (tailDigits) {
      const parsedTail = Number(tailDigits);
      if (!Number.isNaN(parsedTail) && item.orderId === parsedTail) return true;
    }
    return false;
  });
}

export function resolveMockDiagnosticExecutions(
  params?: GetDiagnosticExecutionsParams,
): DiagnosticServiceExecutionListResponseDto {
  const allItems = cloneExecutionItems(mockExecutionState);
  const summary = computeExecutionSummary(allItems);

  const statusFilter = params?.status?.trim().toUpperCase();
  const queueStatusFilter = params?.queueStatus?.trim().toUpperCase();
  const categoryFilter = params?.category?.trim().toUpperCase();
  const priorityFilter = params?.priority?.trim().toUpperCase();
  const keyword = params?.keyword?.trim().toLowerCase() ?? '';
  const readyToExecuteOnly = Boolean(params?.readyToExecuteOnly);
  const activeOnly = Boolean(params?.activeOnly);

  let filtered = allItems.filter((item) => {
    if (readyToExecuteOnly && !item.canStartExecution) return false;
    if (activeOnly && (item.status === 'COMPLETED' || item.status === 'CANCELLED')) return false;
    if (statusFilter && statusFilter !== 'ALL' && item.status.toUpperCase() !== statusFilter)
      return false;
    if (
      queueStatusFilter &&
      queueStatusFilter !== 'ALL' &&
      (item.queueStatus ?? '').toUpperCase() !== queueStatusFilter
    )
      return false;
    if (
      categoryFilter &&
      categoryFilter !== 'ALL' &&
      item.category.toUpperCase() !== categoryFilter
    )
      return false;
    if (
      priorityFilter &&
      priorityFilter !== 'ALL' &&
      item.priority.toUpperCase() !== priorityFilter
    )
      return false;
    if (params?.visitId && item.visitId !== params.visitId) return false;
    if (params?.visitCode && item.visitCode.toUpperCase() !== params.visitCode.trim().toUpperCase())
      return false;
    if (params?.serviceId && item.serviceId !== params.serviceId) return false;
    if (params?.roomId && item.roomId !== params.roomId) return false;
    if (params?.servicePointId && item.servicePointId !== params.servicePointId) return false;
    if (
      params?.requiredForConclusion !== undefined &&
      item.requiredForConclusion !== params.requiredForConclusion
    )
      return false;
    if (keyword) {
      const haystack = [
        item.orderCode,
        item.ticketNo ?? '',
        item.visitCode,
        item.patient.fullName,
        item.patient.patientCode,
        item.serviceCode,
        item.serviceName,
        item.orderingDoctorName,
        item.roomCode ?? '',
      ]
        .join(' ')
        .toLowerCase();
      if (!haystack.includes(keyword)) return false;
    }
    return true;
  });

  if (params?.limit && params.limit > 0) {
    filtered = filtered.slice(0, params.limit);
  }

  return {
    hospitalId: DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK.hospitalId,
    hospitalCode: DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK.hospitalCode,
    hospitalName: DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK.hospitalName,
    facilityName: DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK.hospitalName,
    facilityCode: DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK.hospitalCode,
    departmentId: DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK.departmentId,
    departmentName: DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK.departmentName,
    generatedAtUtc: new Date().toISOString(),
    staffProfile: { ...DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK },
    summary,
    availableServicePoints: DIAGNOSTIC_EXECUTION_SERVICE_POINTS_MOCK.map((sp) => ({ ...sp })),
    items: filtered,
  };
}

export function resolveMockDiagnosticExecutionDetail(
  idOrCode: string,
): DiagnosticServiceExecutionDetailResponseDto {
  const matched = findMockExecutionByTarget(idOrCode) ?? mockExecutionState[0];
  const orderCopy: DiagnosticServiceExecutionItemDto = {
    ...matched,
    patient: { ...matched.patient },
    eligibility: {
      ...matched.eligibility,
      allowedTargetStatuses: [...matched.eligibility.allowedTargetStatuses],
    },
    allowedTargetStatuses: [...matched.allowedTargetStatuses],
  };

  const clinicalContext: DiagnosticOrderClinicalSummaryDto = CLINICAL_CONTEXT_BY_VISIT_ID[
    orderCopy.visitId
  ] ?? {
    visitId: orderCopy.visitId,
    visitCode: orderCopy.visitCode,
    visitStatus: orderCopy.visitStatus,
    reasonForVisit: 'Khám chuyên khoa theo chỉ định lâm sàng',
    presentingComplaint: 'Theo dõi triệu chứng lâm sàng cần cận lâm sàng hỗ trợ chẩn đoán.',
    clinicalFindings: 'Sinh hiệu ổn định, bệnh nhân tỉnh táo tiếp xúc tốt.',
    primaryDiagnosisCode: 'Z00.0',
    primaryDiagnosisName: 'Khám nội khoa tổng quát',
    clinicalDiagnosis: 'Cần thực hiện cận lâm sàng theo phiếu chỉ định của bác sĩ.',
  };

  const history = mockExecutionHistory
    .filter((h) => h.orderId === orderCopy.orderId)
    .map((h) => ({ ...h }));

  return {
    hospitalId: DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK.hospitalId,
    hospitalCode: DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK.hospitalCode,
    hospitalName: DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK.hospitalName,
    generatedAtUtc: new Date().toISOString(),
    staffProfile: { ...DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK },
    order: orderCopy,
    clinicalContext: { ...clinicalContext },
    availableServicePoints: DIAGNOSTIC_EXECUTION_SERVICE_POINTS_MOCK.map((sp) => ({ ...sp })),
    executionHistory: history,
  };
}

const EXECUTION_TRANSITION_ACTIONS: Record<string, string> = {
  IN_PROGRESS: 'DIAGNOSTIC_SERVICE_EXECUTION_STARTED',
  COMPLETED: 'DIAGNOSTIC_SERVICE_EXECUTION_COMPLETED',
  CANCELLED: 'DIAGNOSTIC_SERVICE_EXECUTION_CANCELLED',
  CONFIRMED: 'DIAGNOSTIC_SERVICE_STATUS_UPDATED',
  READY: 'DIAGNOSTIC_SERVICE_STATUS_UPDATED',
  WAITING: 'DIAGNOSTIC_SERVICE_STATUS_UPDATED',
};

const EXECUTION_TRANSITION_ACTION_LABELS: Record<string, string> = {
  IN_PROGRESS: 'Bắt đầu thực hiện kỹ thuật',
  COMPLETED: 'Hoàn tất thực hiện kỹ thuật',
  CANCELLED: 'Hủy thực hiện kỹ thuật',
  CONFIRMED: 'Tạm dừng về đã xác nhận',
  READY: 'Tạm dừng về sẵn sàng thực hiện',
  WAITING: 'Tạm dừng về chờ thực hiện',
};

function applyMockExecutionTransition(
  target: DiagnosticServiceExecutionItemDto,
  payload: UpdateDiagnosticServiceStatusRequest,
): DiagnosticServiceExecutionItemDto {
  const normalizedTarget = (payload.targetStatus ?? payload.status ?? '').trim().toUpperCase();
  if (!normalizedTarget) {
    throw new Error('Trạng thái thực hiện kỹ thuật đích không hợp lệ.');
  }

  const previousStatus = target.status;
  const previousQueueStatus = target.queueStatus;
  const previousStateVersion = target.stateVersion;

  if (
    payload.expectedStateVersion !== undefined &&
    payload.expectedStateVersion !== null &&
    payload.expectedStateVersion !== target.stateVersion
  ) {
    throw new Error(
      `Dữ liệu phiếu chỉ định '${target.orderCode}' đã thay đổi (phiên bản hiện tại v${target.stateVersion}, yêu cầu v${payload.expectedStateVersion}). Vui lòng tải lại.`,
    );
  }

  if (previousStatus === 'ORDERED') {
    throw new Error(
      `Phiếu chỉ định '${target.orderCode}' chưa được xác nhận (UC39). Cần xác nhận trước khi thực hiện kỹ thuật.`,
    );
  }
  if (previousStatus === 'COMPLETED' || previousStatus === 'CANCELLED') {
    throw new Error(
      `Phiếu chỉ định '${target.orderCode}' đã ở trạng thái kết thúc ('${previousStatus}'), không thể cập nhật.`,
    );
  }

  const reason = payload.reason?.trim() ?? '';
  if (
    (normalizedTarget === 'CONFIRMED' ||
      normalizedTarget === 'READY' ||
      normalizedTarget === 'WAITING' ||
      normalizedTarget === 'CANCELLED') &&
    (reason.length < 5 || reason.length > 500)
  ) {
    throw new Error('Lý do tạm dừng / hủy thực hiện kỹ thuật phải từ 5 đến 500 ký tự.');
  }

  const nowIso = new Date().toISOString();
  let nextStatus = normalizedTarget;
  let nextQueueStatus: string;

  if (previousStatus === 'CONFIRMED') {
    if (previousQueueStatus === 'SKIPPED') {
      throw new Error(
        `Lượt hàng đợi '${target.ticketNo}' đang ở trạng thái SKIPPED — cần khôi phục lượt trước khi bắt đầu thực hiện.`,
      );
    }
    if (normalizedTarget === 'IN_PROGRESS') {
      nextQueueStatus = 'IN_PROGRESS';
      target.startedAt = nowIso;
      target.startedByStaffId = DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK.userId;
      target.startedByStaffCode = DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK.staffCode ?? 'DIAG-009';
      target.startedByStaffName = DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK.fullName;
      target.calledAt = nowIso;
      target.presenceStatus = 'PRESENT';
    } else if (normalizedTarget === 'CANCELLED') {
      nextQueueStatus = 'CANCELLED';
      target.presenceStatus = 'LEFT';
    } else {
      throw new Error(`Không thể chuyển phiếu chỉ định từ 'CONFIRMED' sang '${normalizedTarget}'.`);
    }
  } else {
    // previousStatus === 'IN_PROGRESS'
    if (normalizedTarget === 'COMPLETED') {
      nextQueueStatus = 'COMPLETED';
      target.completedAt = nowIso;
      target.completedByStaffId = DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK.userId;
      target.completedByStaffCode = DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK.staffCode ?? 'DIAG-009';
      target.completedByStaffName = DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK.fullName;
    } else if (
      normalizedTarget === 'CONFIRMED' ||
      normalizedTarget === 'READY' ||
      normalizedTarget === 'WAITING'
    ) {
      nextStatus = 'CONFIRMED';
      nextQueueStatus = normalizedTarget === 'CONFIRMED' ? 'WAITING' : normalizedTarget;
      target.startedAt = null;
      target.startedByStaffId = null;
      target.startedByStaffCode = null;
      target.startedByStaffName = null;
      target.presenceStatus = 'PRESENT';
    } else if (normalizedTarget === 'CANCELLED') {
      nextQueueStatus = 'CANCELLED';
      target.presenceStatus = 'LEFT';
    } else {
      throw new Error(
        `Không thể chuyển phiếu chỉ định từ 'IN_PROGRESS' sang '${normalizedTarget}'.`,
      );
    }
  }

  target.status = nextStatus;
  target.statusLabel = ORDER_STATUS_LABELS[nextStatus] ?? nextStatus;
  target.queueStatus = nextQueueStatus;
  target.queueStatusLabel = QUEUE_STATUS_LABELS[nextQueueStatus] ?? nextQueueStatus;
  target.lastTransitionReason = reason || null;
  target.stateVersion = previousStateVersion + 1;

  if (payload.specimenCode?.trim()) target.specimenCode = payload.specimenCode.trim();
  if (payload.specimenType?.trim()) target.specimenType = payload.specimenType.trim();
  if (payload.equipmentCode?.trim()) target.equipmentCode = payload.equipmentCode.trim();
  if (payload.executionNote?.trim()) target.executionNote = payload.executionNote.trim();

  const eligibility = buildEligibility({
    orderId: target.orderId,
    orderCode: target.orderCode,
    visitId: target.visitId,
    visitCode: target.visitCode,
    patientCode: target.patient.patientCode,
    patientName: target.patient.fullName,
    patientDob: target.patient.dateOfBirth ?? '',
    patientAge: target.patient.age ?? 0,
    patientGender: target.patient.gender ?? '',
    serviceCode: target.serviceCode,
    serviceName: target.serviceName,
    category: (target.category as 'LABORATORY' | 'IMAGING' | 'FUNCTIONAL') ?? 'LABORATORY',
    priority: (target.priority as 'NORMAL' | 'URGENT' | 'STAT') ?? 'NORMAL',
    status: target.status as ExecutionSeed['status'],
    queueStatus: (target.queueStatus ?? 'WAITING') as ExecutionSeed['queueStatus'],
    ticketNo: target.ticketNo ?? `C${target.orderId}`,
    queueNumber: target.queueNumber ?? target.orderId,
    roomId: target.roomId ?? 0,
    roomCode: target.roomCode ?? '',
    roomName: target.roomName ?? '',
    servicePointId: target.servicePointId ?? 0,
    servicePointName: target.servicePointName ?? '',
    stateVersion: target.stateVersion,
    orderedAt: target.orderedAt,
    unitPrice: target.unitPrice,
    doctorName: target.orderingDoctorName,
  });
  target.canUpdateStatus = eligibility.canUpdateStatus;
  target.canStartExecution = eligibility.canStartExecution;
  target.canCompleteExecution = eligibility.canCompleteExecution;
  target.canPauseExecution = eligibility.canPauseExecution;
  target.canCancelExecution = eligibility.canCancelExecution;
  target.canEnterResult = target.status === 'COMPLETED';
  target.disabledReason = eligibility.disabledReason;
  target.allowedTargetStatuses = eligibility.allowedTargetStatuses;
  target.eligibility = eligibility;

  const historyEntry: DiagnosticServiceExecutionHistoryItemDto = {
    auditLogId: mockAuditLogSeq++,
    orderId: target.orderId,
    orderCode: target.orderCode,
    queueEntryId: target.queueEntryId,
    action: EXECUTION_TRANSITION_ACTIONS[normalizedTarget] ?? 'DIAGNOSTIC_SERVICE_STATUS_UPDATED',
    actionLabel:
      EXECUTION_TRANSITION_ACTION_LABELS[normalizedTarget] ?? 'Cập nhật trạng thái thực hiện',
    previousStatus,
    currentStatus: nextStatus,
    previousQueueStatus: previousQueueStatus ?? null,
    currentQueueStatus: nextQueueStatus,
    previousStateVersion,
    stateVersion: target.stateVersion,
    specimenCode: target.specimenCode ?? null,
    specimenType: target.specimenType ?? null,
    equipmentCode: target.equipmentCode ?? null,
    executionNote: target.executionNote ?? null,
    reason: reason || null,
    actorUserId: DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK.userId,
    performedByStaffId: DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK.userId,
    performedByStaffCode: DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK.staffCode ?? 'DIAG-009',
    performedByStaffName: DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK.fullName,
    occurredAtUtc: nowIso,
  };
  mockExecutionHistory = [...mockExecutionHistory, historyEntry];

  return {
    ...target,
    patient: { ...target.patient },
    eligibility: {
      ...target.eligibility,
      allowedTargetStatuses: [...target.eligibility.allowedTargetStatuses],
    },
    allowedTargetStatuses: [...target.allowedTargetStatuses],
  };
}

export function updateMockDiagnosticServiceStatus(
  idOrCode: string,
  payload: UpdateDiagnosticServiceStatusRequest,
): UpdateDiagnosticServiceStatusResponseDto {
  const matched = findMockExecutionByTarget(idOrCode);
  if (!matched) {
    throw new Error(`Không tìm thấy phiếu chỉ định cận lâm sàng '${idOrCode}'.`);
  }

  const previousStatus = matched.status;
  const updated = applyMockExecutionTransition(matched, payload);
  const action =
    EXECUTION_TRANSITION_ACTIONS[
      (payload.targetStatus ?? payload.status ?? '').trim().toUpperCase()
    ] ?? 'DIAGNOSTIC_SERVICE_STATUS_UPDATED';

  return {
    actionOutcome:
      action === 'DIAGNOSTIC_SERVICE_STATUS_UPDATED'
        ? 'STATUS_UPDATED'
        : action.replace('DIAGNOSTIC_SERVICE_EXECUTION_', ''),
    action,
    message: `Đã cập nhật trạng thái thực hiện phiếu chỉ định '${updated.orderCode}' từ '${previousStatus}' sang '${updated.status}'.`,
    updatedCount: 1,
    orderId: updated.orderId,
    orderCode: updated.orderCode,
    queueEntryId: updated.queueEntryId,
    formattedQueueEntryId: updated.formattedQueueEntryId,
    ticketNo: updated.ticketNo,
    previousStatus,
    currentStatus: updated.status,
    previousQueueStatus:
      mockExecutionHistory[mockExecutionHistory.length - 1]?.previousQueueStatus ?? null,
    currentQueueStatus: updated.queueStatus ?? null,
    previousStateVersion:
      mockExecutionHistory[mockExecutionHistory.length - 1]?.previousStateVersion ??
      updated.stateVersion - 1,
    stateVersion: updated.stateVersion,
    specimenCode: updated.specimenCode,
    specimenType: updated.specimenType,
    equipmentCode: updated.equipmentCode,
    executionNote: updated.executionNote,
    reason: updated.lastTransitionReason,
    updatedAtUtc: new Date().toISOString(),
    updatedByStaff: { ...DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK },
    order: updated,
    updatedOrders: [updated],
  };
}

export function startMockDiagnosticServiceExecution(
  idOrCode: string,
  payload?: UpdateDiagnosticServiceStatusRequest,
): UpdateDiagnosticServiceStatusResponseDto {
  return updateMockDiagnosticServiceStatus(idOrCode, {
    ...payload,
    targetStatus: 'IN_PROGRESS',
  });
}

export function completeMockDiagnosticServiceExecution(
  idOrCode: string,
  payload?: UpdateDiagnosticServiceStatusRequest,
): UpdateDiagnosticServiceStatusResponseDto {
  return updateMockDiagnosticServiceStatus(idOrCode, {
    ...payload,
    targetStatus: 'COMPLETED',
  });
}

export function updateMockDiagnosticServiceStatusBatch(
  payload: UpdateDiagnosticServiceStatusBatchRequest,
): UpdateDiagnosticServiceStatusResponseDto {
  const targetKeys: Array<string | number> = [];
  if (payload.orderId) targetKeys.push(payload.orderId);
  if (payload.orderCode) targetKeys.push(payload.orderCode);
  if (payload.orderIds?.length) targetKeys.push(...payload.orderIds);
  if (payload.orderCodes?.length) targetKeys.push(...payload.orderCodes);

  if (targetKeys.length === 0 && (payload.visitId || payload.visitCode)) {
    const visitCodeUpper = payload.visitCode?.trim().toUpperCase();
    for (const item of mockExecutionState) {
      if (
        item.canUpdateStatus &&
        ((payload.visitId && item.visitId === payload.visitId) ||
          (visitCodeUpper && item.visitCode.toUpperCase() === visitCodeUpper))
      ) {
        targetKeys.push(item.orderId);
      }
    }
  }

  const updatedOrders: DiagnosticServiceExecutionItemDto[] = [];
  let previousStatus = '';
  let previousStateVersion = 0;

  for (const key of targetKeys) {
    const matched = findMockExecutionByTarget(key);
    if (!matched) {
      throw new Error(`Không tìm thấy phiếu chỉ định cận lâm sàng '${key}'.`);
    }
    previousStatus = matched.status;
    previousStateVersion = matched.stateVersion;
    updatedOrders.push(applyMockExecutionTransition(matched, payload));
  }

  if (updatedOrders.length === 0) {
    throw new Error(
      'Vui lòng chọn ít nhất một phiếu chỉ định cận lâm sàng để cập nhật trạng thái.',
    );
  }

  const primaryOrder = updatedOrders[0];
  const normalizedTarget = (payload.targetStatus ?? payload.status ?? '').trim().toUpperCase();
  const action =
    EXECUTION_TRANSITION_ACTIONS[normalizedTarget] ?? 'DIAGNOSTIC_SERVICE_STATUS_UPDATED';

  return {
    actionOutcome: updatedOrders.length > 1 ? 'BATCH_STATUS_UPDATED' : 'STATUS_UPDATED',
    action,
    message: `Đã cập nhật trạng thái thực hiện cho ${updatedOrders.length} phiếu chỉ định sang '${normalizedTarget}'.`,
    updatedCount: updatedOrders.length,
    orderId: primaryOrder.orderId,
    orderCode: primaryOrder.orderCode,
    queueEntryId: primaryOrder.queueEntryId,
    formattedQueueEntryId: primaryOrder.formattedQueueEntryId,
    ticketNo: primaryOrder.ticketNo,
    previousStatus,
    currentStatus: primaryOrder.status,
    previousQueueStatus: null,
    currentQueueStatus: primaryOrder.queueStatus ?? null,
    previousStateVersion,
    stateVersion: primaryOrder.stateVersion,
    specimenCode: primaryOrder.specimenCode,
    specimenType: primaryOrder.specimenType,
    equipmentCode: primaryOrder.equipmentCode,
    executionNote: primaryOrder.executionNote,
    reason: primaryOrder.lastTransitionReason,
    updatedAtUtc: new Date().toISOString(),
    updatedByStaff: { ...DIAGNOSTIC_EXECUTION_STAFF_PROFILE_MOCK },
    order: primaryOrder,
    updatedOrders,
  };
}
