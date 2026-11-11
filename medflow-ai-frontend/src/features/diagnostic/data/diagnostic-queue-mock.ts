import { DIAGNOSTIC_QUICK_ACTIONS_MOCK } from './diagnostic-mock-data';
import type {
  CallNextDiagnosticPatientRequest,
  CallNextDiagnosticPatientResponseDto,
  CancelDiagnosticQueueEntryRequest,
  DiagnosticOrderClinicalSummaryDto,
  DiagnosticQueueBoardResponseDto,
  DiagnosticQueueEntryDetailResponseDto,
  DiagnosticQueueEntryItemDto,
  DiagnosticQueueItemActionDto,
  DiagnosticQueueSummaryStatsDto,
  DiagnosticQueueTurnActionRequest,
  DiagnosticServicePointOptionDto,
  DiagnosticServicePointQueueBucketDto,
  DiagnosticStaffProfile,
  GetDiagnosticQueueParams,
  ManageDiagnosticQueueActionResponseDto,
  QueueStatusTransitionHistoryItemDto,
  UpdateDiagnosticQueueStatusRequest,
} from '../types/diagnostic.types';

export const DIAGNOSTIC_QUEUE_STAFF_PROFILE_MOCK: DiagnosticStaffProfile = {
  userId: 304,
  email: 'diagnostic@medflow.vn',
  fullName: 'CN. Lê Hoàng Minh',
  staffCode: 'DIAG-009',
  hospitalId: 1,
  hospitalCode: 'BV-DH-YD',
  hospitalName: 'Bệnh viện Đại học Y Dược MedFlow',
  departmentId: 12,
  departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
  roles: ['DIAGNOSTIC_STAFF'],
  permissions: [
    'DIAGNOSTIC_RESULT_READ',
    'DIAGNOSTIC_RESULT_WRITE',
    'DIAGNOSTIC_ORDER',
    'QUEUE_VIEW',
    'QUEUE_MANAGE',
  ],
};

export const DIAGNOSTIC_QUEUE_SERVICE_POINT_OPTIONS_MOCK: DiagnosticServicePointOptionDto[] = [
  {
    servicePointId: 201,
    name: 'Điểm lấy mẫu & Sinh hóa huyết học 01',
    serviceId: 501,
    roomId: 21,
    roomCode: 'P.XN-201',
    roomName: 'Phòng Xét nghiệm 201',
    roomLocation: 'Tầng 2 - Khu B',
    isActive: true,
    isRoomActive: true,
  },
  {
    servicePointId: 204,
    name: 'Buồng chụp X-quang & CT-Scanner 04',
    serviceId: 504,
    roomId: 24,
    roomCode: 'P.XQ-204',
    roomName: 'Phòng X-quang 204',
    roomLocation: 'Tầng 2 - Khu C',
    isActive: true,
    isRoomActive: true,
  },
  {
    servicePointId: 205,
    name: 'Buồng Điện tâm đồ & Thăm dò chức năng 05',
    serviceId: 502,
    roomId: 25,
    roomCode: 'P.TDCN-205',
    roomName: 'Phòng Thăm dò chức năng 205',
    roomLocation: 'Tầng 2 - Khu A',
    isActive: true,
    isRoomActive: true,
  },
];

const ALLOWED_TRANSITIONS: Record<string, string[]> = {
  RESERVED: ['WAITING', 'READY', 'CANCELLED'],
  WAITING: ['READY', 'CALLED', 'IN_PROGRESS', 'SKIPPED', 'CANCELLED'],
  READY: ['WAITING', 'CALLED', 'IN_PROGRESS', 'SKIPPED', 'CANCELLED'],
  CALLED: ['IN_PROGRESS', 'READY', 'WAITING', 'SKIPPED', 'CANCELLED'],
  IN_PROGRESS: ['COMPLETED', 'WAITING', 'READY', 'SKIPPED'],
  SKIPPED: ['READY', 'WAITING', 'CALLED', 'CANCELLED'],
  COMPLETED: [],
  CANCELLED: [],
};

const STATUS_LABELS: Record<string, string> = {
  RESERVED: 'Đã giữ chỗ',
  WAITING: 'Đang chờ tiếp nhận',
  READY: 'Sẵn sàng gọi',
  CALLED: 'Đã gọi vào phòng',
  IN_PROGRESS: 'Đang thực hiện kỹ thuật',
  COMPLETED: 'Đã hoàn tất kỹ thuật',
  SKIPPED: 'Qua lượt / Vắng mặt',
  CANCELLED: 'Đã hủy lượt',
};

const PRESENCE_LABELS: Record<string, string> = {
  PRESENT: 'Có mặt tại khu CLS',
  NOT_ARRIVED: 'Chưa đến khu CLS',
  LEFT: 'Tạm rời khu CLS',
};

function buildAllowedActions(
  status: string,
  presenceStatus: string,
  orderStatus: string,
): {
  allowedTargetStatuses: string[];
  canReceive: boolean;
  canCall: boolean;
  canStart: boolean;
  canComplete: boolean;
  canSkip: boolean;
  canRestore: boolean;
  canCancel: boolean;
  isEligible: boolean;
  ineligibleReason: string | null;
  allowedActions: DiagnosticQueueItemActionDto[];
} {
  const allowedTargetStatuses = ALLOWED_TRANSITIONS[status] ?? [];
  const isOrderConfirmedOrActive = orderStatus === 'CONFIRMED' || orderStatus === 'IN_PROGRESS';
  const canReceive = status === 'WAITING' || status === 'RESERVED' || presenceStatus !== 'PRESENT';
  const canCall = isOrderConfirmedOrActive && (status === 'READY' || status === 'WAITING');
  const canStart = isOrderConfirmedOrActive && (status === 'CALLED' || status === 'READY');
  const canComplete = status === 'IN_PROGRESS';
  const canSkip = status === 'CALLED' || status === 'READY' || status === 'WAITING';
  const canRestore = status === 'SKIPPED';
  const canCancel = status !== 'IN_PROGRESS' && status !== 'COMPLETED' && status !== 'CANCELLED';

  const isEligible =
    isOrderConfirmedOrActive &&
    (status === 'READY' || status === 'WAITING') &&
    presenceStatus === 'PRESENT';

  let ineligibleReason: string | null = null;
  if (!isOrderConfirmedOrActive) {
    ineligibleReason = 'Phiếu chỉ định chưa được xác nhận (ORDERED).';
  } else if (status === 'SKIPPED') {
    ineligibleReason = 'Bệnh nhân đã qua lượt, cần khôi phục lượt chờ trước khi gọi.';
  } else if (status === 'COMPLETED' || status === 'CANCELLED') {
    ineligibleReason = 'Lượt xếp hàng đã kết thúc.';
  } else if (presenceStatus !== 'PRESENT' && status !== 'IN_PROGRESS') {
    ineligibleReason = 'Bệnh nhân chưa xác nhận có mặt tại điểm phục vụ cận lâm sàng.';
  }

  const allowedActions: DiagnosticQueueItemActionDto[] = [
    {
      id: 'receive',
      label: 'Tiếp nhận vào hàng đợi',
      ucCode: 'UC40',
      enabled: canReceive,
      disabledReason: canReceive ? null : 'Bệnh nhân đã sẵn sàng trong hàng đợi.',
    },
    {
      id: 'call',
      label: 'Gọi bệnh nhân',
      ucCode: 'UC41',
      enabled: canCall,
      disabledReason: canCall ? null : ineligibleReason,
    },
    {
      id: 'start',
      label: 'Bắt đầu thực hiện',
      ucCode: 'UC41',
      enabled: canStart,
      disabledReason: canStart ? null : ineligibleReason,
    },
    {
      id: 'complete',
      label: 'Hoàn tất kỹ thuật',
      ucCode: 'UC41',
      enabled: canComplete,
      disabledReason: canComplete
        ? null
        : 'Chỉ hoàn tất khi đang thực hiện kỹ thuật (IN_PROGRESS).',
    },
    {
      id: 'missed-turn',
      label: 'Đánh dấu qua lượt',
      ucCode: 'UC41',
      enabled: canSkip,
      disabledReason: canSkip ? null : 'Không thể đánh dấu qua lượt ở trạng thái hiện tại.',
    },
    {
      id: 'restore-turn',
      label: 'Khôi phục lượt chờ',
      ucCode: 'UC41',
      enabled: canRestore,
      disabledReason: canRestore ? null : 'Chỉ khôi phục cho lượt đang ở trạng thái SKIPPED.',
    },
    {
      id: 'cancel',
      label: 'Hủy lượt xếp hàng',
      ucCode: 'UC41',
      enabled: canCancel,
      disabledReason: canCancel ? null : 'Không thể hủy lượt đang thực hiện hoặc đã hoàn tất.',
    },
  ];

  return {
    allowedTargetStatuses,
    canReceive,
    canCall,
    canStart,
    canComplete,
    canSkip,
    canRestore,
    canCancel,
    isEligible,
    ineligibleReason,
    allowedActions,
  };
}

function createInitialEntries(): DiagnosticQueueEntryItemDto[] {
  const rawEntries: Array<
    Omit<
      DiagnosticQueueEntryItemDto,
      | 'allowedTargetStatuses'
      | 'canReceive'
      | 'canCall'
      | 'canStart'
      | 'canComplete'
      | 'canSkip'
      | 'canRestore'
      | 'canCancel'
      | 'isEligible'
      | 'ineligibleReason'
      | 'allowedActions'
    >
  > = [
    {
      id: 'QE-00009041',
      queueEntryId: 9041,
      formattedQueueEntryId: 'QE-00009041',
      queueNumber: 18,
      ticketNo: 'C018',
      servingOrder: 1,
      queueDate: '2026-10-20',
      queueType: 'DIAGNOSTIC',
      status: 'READY',
      statusLabel: STATUS_LABELS.READY,
      presenceStatus: 'PRESENT',
      presenceStatusLabel: PRESENCE_LABELS.PRESENT,
      arrivalClass: 'ON_TIME',
      clinicalPriority: 'STAT',
      priority: 'STAT',
      priorityLabel: 'Cấp cứu (STAT)',
      stateVersion: 2,
      scheduledAt: '2026-10-20T08:00:00Z',
      arrivedAt: '2026-10-20T08:02:00Z',
      readyAt: '2026-10-20T08:04:00Z',
      calledAt: null,
      startedAt: null,
      completedAt: null,
      blockedReason: null,
      estimatedWaitMinutes: 0,
      estimatedWaitLabel: 'Gọi ngay (~0 phút)',
      orderId: 41,
      orderCode: 'DIA-202610-0041',
      orderStatus: 'CONFIRMED',
      orderStatusLabel: 'Đã xác nhận',
      requiredForConclusion: true,
      instruction: 'Lấy máu tĩnh mạch khẩn kiểm tra Troponin I & Sinh hóa máu cấp cứu.',
      orderedAt: '2026-10-20T07:55:00Z',
      confirmedAt: '2026-10-20T08:01:00Z',
      confirmedByStaffId: 304,
      confirmedByStaffCode: 'DIAG-009',
      confirmedByStaffName: 'CN. Lê Hoàng Minh',
      visitId: 1001,
      visitCode: 'VIS-202610-1001',
      visitStatus: 'WAITING_DIAGNOSTIC',
      patientId: 2001,
      patientCode: 'PAT-2026-0001',
      patientName: 'Phạm Thu Dung',
      patientGender: 'FEMALE',
      patientGenderLabel: 'Nữ',
      patientDateOfBirth: '1962-04-15',
      patientAge: 64,
      bloodType: 'O+',
      allergiesSummary: 'Dị ứng thuốc cản quang chứa I-ốt mức độ nhẹ',
      serviceId: 501,
      serviceCode: 'LAB-BIO-01',
      serviceName: 'Sinh hóa máu: Glucose, HbA1c, Creatinin, Troponin I',
      category: 'LABORATORY',
      categoryLabel: 'Xét nghiệm',
      departmentId: 12,
      departmentCode: 'KHOA-CLS',
      departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
      specialtyId: 3,
      specialtyCode: 'NOI-TIM',
      specialtyName: 'Nội Tim mạch',
      orderingDoctorId: 101,
      orderingDoctorCode: 'DOC-001',
      orderingDoctorName: 'BS.CKII Trần Khánh',
      servicePointId: 201,
      formattedServicePointId: 'SP-201',
      servicePointName: 'Điểm lấy mẫu & Sinh hóa huyết học 01',
      roomId: 21,
      roomCode: 'P.XN-201',
      roomName: 'Phòng Xét nghiệm 201',
      roomLocation: 'Tầng 2 - Khu B',
      hasResult: false,
      isResultFinalized: false,
      resultStatus: 'PENDING',
    },
    {
      id: 'QE-00009042',
      queueEntryId: 9042,
      formattedQueueEntryId: 'QE-00009042',
      queueNumber: 19,
      ticketNo: 'C019',
      servingOrder: 2,
      queueDate: '2026-10-20',
      queueType: 'DIAGNOSTIC',
      status: 'WAITING',
      statusLabel: STATUS_LABELS.WAITING,
      presenceStatus: 'PRESENT',
      presenceStatusLabel: PRESENCE_LABELS.PRESENT,
      arrivalClass: 'ON_TIME',
      clinicalPriority: 'URGENT',
      priority: 'URGENT',
      priorityLabel: 'Khẩn (URGENT)',
      stateVersion: 1,
      scheduledAt: '2026-10-20T08:10:00Z',
      arrivedAt: '2026-10-20T08:12:00Z',
      readyAt: null,
      calledAt: null,
      startedAt: null,
      completedAt: null,
      blockedReason: null,
      estimatedWaitMinutes: 10,
      estimatedWaitLabel: '~10 phút',
      orderId: 42,
      orderCode: 'DIA-202610-0042',
      orderStatus: 'CONFIRMED',
      orderStatusLabel: 'Đã xác nhận',
      requiredForConclusion: true,
      instruction: 'Bệnh nhân nhịn ăn sáng, lấy 2 ống EDTA + 1 ống Serum.',
      orderedAt: '2026-10-20T08:05:00Z',
      confirmedAt: '2026-10-20T08:11:00Z',
      confirmedByStaffId: 304,
      confirmedByStaffCode: 'DIAG-009',
      confirmedByStaffName: 'CN. Lê Hoàng Minh',
      visitId: 1002,
      visitCode: 'VIS-202610-1002',
      visitStatus: 'WAITING_DIAGNOSTIC',
      patientId: 2002,
      patientCode: 'PAT-2026-0002',
      patientName: 'Nguyễn Văn Hùng',
      patientGender: 'MALE',
      patientGenderLabel: 'Nam',
      patientDateOfBirth: '1975-09-20',
      patientAge: 51,
      bloodType: 'A+',
      allergiesSummary: null,
      serviceId: 501,
      serviceCode: 'LAB-CBC-02',
      serviceName: 'Tổng phân tích tế bào máu ngoại vi (Công thức máu 24 thông số)',
      category: 'LABORATORY',
      categoryLabel: 'Xét nghiệm',
      departmentId: 12,
      departmentCode: 'KHOA-CLS',
      departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
      specialtyId: 1,
      specialtyCode: 'NOI-TQ',
      specialtyName: 'Nội Tổng quát',
      orderingDoctorId: 102,
      orderingDoctorCode: 'DOC-002',
      orderingDoctorName: 'ThS.BS Lê Hạnh',
      servicePointId: 201,
      formattedServicePointId: 'SP-201',
      servicePointName: 'Điểm lấy mẫu & Sinh hóa huyết học 01',
      roomId: 21,
      roomCode: 'P.XN-201',
      roomName: 'Phòng Xét nghiệm 201',
      roomLocation: 'Tầng 2 - Khu B',
      hasResult: false,
      isResultFinalized: false,
      resultStatus: 'PENDING',
    },
    {
      id: 'QE-00009043',
      queueEntryId: 9043,
      formattedQueueEntryId: 'QE-00009043',
      queueNumber: 20,
      ticketNo: 'C020',
      servingOrder: 1,
      queueDate: '2026-10-20',
      queueType: 'DIAGNOSTIC',
      status: 'CALLED',
      statusLabel: STATUS_LABELS.CALLED,
      presenceStatus: 'PRESENT',
      presenceStatusLabel: PRESENCE_LABELS.PRESENT,
      arrivalClass: 'ON_TIME',
      clinicalPriority: 'URGENT',
      priority: 'URGENT',
      priorityLabel: 'Khẩn (URGENT)',
      stateVersion: 3,
      scheduledAt: '2026-10-20T08:05:00Z',
      arrivedAt: '2026-10-20T08:07:00Z',
      readyAt: '2026-10-20T08:09:00Z',
      calledAt: '2026-10-20T08:15:00Z',
      startedAt: null,
      completedAt: null,
      blockedReason: null,
      estimatedWaitMinutes: 0,
      estimatedWaitLabel: 'Đang gọi vào phòng',
      orderId: 43,
      orderCode: 'DIA-202610-0043',
      orderStatus: 'CONFIRMED',
      orderStatusLabel: 'Đã xác nhận',
      requiredForConclusion: true,
      instruction: 'Tháo trang sức kim loại vùng ngực trước khi vào buồng chụp X-quang.',
      orderedAt: '2026-10-20T08:00:00Z',
      confirmedAt: '2026-10-20T08:06:00Z',
      confirmedByStaffId: 304,
      confirmedByStaffCode: 'DIAG-009',
      confirmedByStaffName: 'CN. Lê Hoàng Minh',
      visitId: 1003,
      visitCode: 'VIS-202610-1003',
      visitStatus: 'WAITING_DIAGNOSTIC',
      patientId: 2003,
      patientCode: 'PAT-2026-0003',
      patientName: 'Trần Thị Mai Hương',
      patientGender: 'FEMALE',
      patientGenderLabel: 'Nữ',
      patientDateOfBirth: '1988-11-02',
      patientAge: 38,
      bloodType: 'B+',
      allergiesSummary: 'Tiền sử hen phế quản dị ứng thời tiết',
      serviceId: 504,
      serviceCode: 'IMG-XRAY-01',
      serviceName: 'Chụp X-quang ngực thẳng kỹ thuật số (DR)',
      category: 'IMAGING',
      categoryLabel: 'Chẩn đoán hình ảnh',
      departmentId: 12,
      departmentCode: 'KHOA-CLS',
      departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
      specialtyId: 2,
      specialtyCode: 'NOI-HH',
      specialtyName: 'Nội Hô hấp',
      orderingDoctorId: 103,
      orderingDoctorCode: 'DOC-003',
      orderingDoctorName: 'BS.CKI Đinh Long',
      servicePointId: 204,
      formattedServicePointId: 'SP-204',
      servicePointName: 'Buồng chụp X-quang & CT-Scanner 04',
      roomId: 24,
      roomCode: 'P.XQ-204',
      roomName: 'Phòng X-quang 204',
      roomLocation: 'Tầng 2 - Khu C',
      hasResult: false,
      isResultFinalized: false,
      resultStatus: 'PENDING',
    },
    {
      id: 'QE-00009044',
      queueEntryId: 9044,
      formattedQueueEntryId: 'QE-00009044',
      queueNumber: 21,
      ticketNo: 'C021',
      servingOrder: 1,
      queueDate: '2026-10-20',
      queueType: 'DIAGNOSTIC',
      status: 'IN_PROGRESS',
      statusLabel: STATUS_LABELS.IN_PROGRESS,
      presenceStatus: 'PRESENT',
      presenceStatusLabel: PRESENCE_LABELS.PRESENT,
      arrivalClass: 'ON_TIME',
      clinicalPriority: 'NORMAL',
      priority: 'NORMAL',
      priorityLabel: 'Bình thường (NORMAL)',
      stateVersion: 4,
      scheduledAt: '2026-10-20T07:50:00Z',
      arrivedAt: '2026-10-20T07:52:00Z',
      readyAt: '2026-10-20T07:55:00Z',
      calledAt: '2026-10-20T08:00:00Z',
      startedAt: '2026-10-20T08:03:00Z',
      completedAt: null,
      blockedReason: null,
      estimatedWaitMinutes: 0,
      estimatedWaitLabel: 'Đang thực hiện kỹ thuật',
      orderId: 44,
      orderCode: 'DIA-202610-0044',
      orderStatus: 'IN_PROGRESS',
      orderStatusLabel: 'Đang thực hiện',
      requiredForConclusion: true,
      instruction: 'Nằm nghỉ 5 phút trước khi đo điện tâm đồ 12 chuyển đạo.',
      orderedAt: '2026-10-20T07:45:00Z',
      confirmedAt: '2026-10-20T07:50:00Z',
      confirmedByStaffId: 304,
      confirmedByStaffCode: 'DIAG-009',
      confirmedByStaffName: 'CN. Lê Hoàng Minh',
      visitId: 1004,
      visitCode: 'VIS-202610-1004',
      visitStatus: 'IN_PROGRESS',
      patientId: 2004,
      patientCode: 'PAT-2026-0004',
      patientName: 'Đặng Quốc Bảo',
      patientGender: 'MALE',
      patientGenderLabel: 'Nam',
      patientDateOfBirth: '1958-02-10',
      patientAge: 68,
      bloodType: 'AB+',
      allergiesSummary: null,
      serviceId: 502,
      serviceCode: 'FNC-ECG-01',
      serviceName: 'Đo Điện tâm đồ 12 chuyển đạo thường quy (ECG)',
      category: 'FUNCTIONAL',
      categoryLabel: 'Thăm dò chức năng',
      departmentId: 12,
      departmentCode: 'KHOA-CLS',
      departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
      specialtyId: 3,
      specialtyCode: 'NOI-TIM',
      specialtyName: 'Nội Tim mạch',
      orderingDoctorId: 101,
      orderingDoctorCode: 'DOC-001',
      orderingDoctorName: 'BS.CKII Trần Khánh',
      servicePointId: 205,
      formattedServicePointId: 'SP-205',
      servicePointName: 'Buồng Điện tâm đồ & Thăm dò chức năng 05',
      roomId: 25,
      roomCode: 'P.TDCN-205',
      roomName: 'Phòng Thăm dò chức năng 205',
      roomLocation: 'Tầng 2 - Khu A',
      hasResult: false,
      isResultFinalized: false,
      resultStatus: 'PENDING',
    },
    {
      id: 'QE-00009045',
      queueEntryId: 9045,
      formattedQueueEntryId: 'QE-00009045',
      queueNumber: 22,
      ticketNo: 'C022',
      servingOrder: 3,
      queueDate: '2026-10-20',
      queueType: 'DIAGNOSTIC',
      status: 'SKIPPED',
      statusLabel: STATUS_LABELS.SKIPPED,
      presenceStatus: 'LEFT',
      presenceStatusLabel: PRESENCE_LABELS.LEFT,
      arrivalClass: 'LATE',
      clinicalPriority: 'NORMAL',
      priority: 'NORMAL',
      priorityLabel: 'Bình thường (NORMAL)',
      stateVersion: 3,
      scheduledAt: '2026-10-20T07:40:00Z',
      arrivedAt: '2026-10-20T07:45:00Z',
      readyAt: '2026-10-20T07:48:00Z',
      calledAt: '2026-10-20T07:55:00Z',
      startedAt: null,
      completedAt: null,
      blockedReason: 'Bệnh nhân vắng mặt khi gọi tên 3 lần tại loa phòng cận lâm sàng.',
      estimatedWaitMinutes: 15,
      estimatedWaitLabel: 'Chờ khôi phục lượt',
      orderId: 45,
      orderCode: 'DIA-202610-0045',
      orderStatus: 'CONFIRMED',
      orderStatusLabel: 'Đã xác nhận',
      requiredForConclusion: false,
      instruction: 'Lấy mẫu nước tiểu giữa dòng sáng sớm.',
      orderedAt: '2026-10-20T07:35:00Z',
      confirmedAt: '2026-10-20T07:42:00Z',
      confirmedByStaffId: 304,
      confirmedByStaffCode: 'DIAG-009',
      confirmedByStaffName: 'CN. Lê Hoàng Minh',
      visitId: 1005,
      visitCode: 'VIS-202610-1005',
      visitStatus: 'WAITING_DIAGNOSTIC',
      patientId: 2005,
      patientCode: 'PAT-2026-0005',
      patientName: 'Lê Thị Bích Ngọc',
      patientGender: 'FEMALE',
      patientGenderLabel: 'Nữ',
      patientDateOfBirth: '1993-07-19',
      patientAge: 33,
      bloodType: 'O+',
      allergiesSummary: null,
      serviceId: 501,
      serviceCode: 'LAB-URI-01',
      serviceName: 'Tổng phân tích nước tiểu 10 thông số tự động',
      category: 'LABORATORY',
      categoryLabel: 'Xét nghiệm',
      departmentId: 12,
      departmentCode: 'KHOA-CLS',
      departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
      specialtyId: 1,
      specialtyCode: 'NOI-TQ',
      specialtyName: 'Nội Tổng quát',
      orderingDoctorId: 102,
      orderingDoctorCode: 'DOC-002',
      orderingDoctorName: 'ThS.BS Lê Hạnh',
      servicePointId: 201,
      formattedServicePointId: 'SP-201',
      servicePointName: 'Điểm lấy mẫu & Sinh hóa huyết học 01',
      roomId: 21,
      roomCode: 'P.XN-201',
      roomName: 'Phòng Xét nghiệm 201',
      roomLocation: 'Tầng 2 - Khu B',
      hasResult: false,
      isResultFinalized: false,
      resultStatus: 'PENDING',
    },
    {
      id: 'QE-00009046',
      queueEntryId: 9046,
      formattedQueueEntryId: 'QE-00009046',
      queueNumber: 17,
      ticketNo: 'C017',
      servingOrder: 2,
      queueDate: '2026-10-20',
      queueType: 'DIAGNOSTIC',
      status: 'COMPLETED',
      statusLabel: STATUS_LABELS.COMPLETED,
      presenceStatus: 'PRESENT',
      presenceStatusLabel: PRESENCE_LABELS.PRESENT,
      arrivalClass: 'ON_TIME',
      clinicalPriority: 'NORMAL',
      priority: 'NORMAL',
      priorityLabel: 'Bình thường (NORMAL)',
      stateVersion: 5,
      scheduledAt: '2026-10-20T07:30:00Z',
      arrivedAt: '2026-10-20T07:31:00Z',
      readyAt: '2026-10-20T07:33:00Z',
      calledAt: '2026-10-20T07:36:00Z',
      startedAt: '2026-10-20T07:38:00Z',
      completedAt: '2026-10-20T07:50:00Z',
      blockedReason: null,
      estimatedWaitMinutes: 0,
      estimatedWaitLabel: 'Đã hoàn tất',
      orderId: 46,
      orderCode: 'DIA-202610-0046',
      orderStatus: 'COMPLETED',
      orderStatusLabel: 'Đã hoàn tất',
      requiredForConclusion: true,
      instruction: 'Đã chụp X-quang khớp gối 2 tư thế thẳng - nghiêng.',
      orderedAt: '2026-10-20T07:25:00Z',
      confirmedAt: '2026-10-20T07:30:00Z',
      confirmedByStaffId: 304,
      confirmedByStaffCode: 'DIAG-009',
      confirmedByStaffName: 'CN. Lê Hoàng Minh',
      visitId: 1006,
      visitCode: 'VIS-202610-1006',
      visitStatus: 'IN_PROGRESS',
      patientId: 2006,
      patientCode: 'PAT-2026-0006',
      patientName: 'Hoàng Minh Tuấn',
      patientGender: 'MALE',
      patientGenderLabel: 'Nam',
      patientDateOfBirth: '1970-12-05',
      patientAge: 56,
      bloodType: 'A+',
      allergiesSummary: null,
      serviceId: 504,
      serviceCode: 'IMG-XRAY-02',
      serviceName: 'Chụp X-quang khớp gối 2 tư thế (Thẳng - Nghiêng)',
      category: 'IMAGING',
      categoryLabel: 'Chẩn đoán hình ảnh',
      departmentId: 12,
      departmentCode: 'KHOA-CLS',
      departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
      specialtyId: 4,
      specialtyCode: 'CO-XUONG',
      specialtyName: 'Cơ Xương Khớp',
      orderingDoctorId: 103,
      orderingDoctorCode: 'DOC-003',
      orderingDoctorName: 'BS.CKI Đinh Long',
      servicePointId: 204,
      formattedServicePointId: 'SP-204',
      servicePointName: 'Buồng chụp X-quang & CT-Scanner 04',
      roomId: 24,
      roomCode: 'P.XQ-204',
      roomName: 'Phòng X-quang 204',
      roomLocation: 'Tầng 2 - Khu C',
      hasResult: true,
      isResultFinalized: true,
      resultStatus: 'FINALIZED',
    },
  ];

  return rawEntries.map((entry) => ({
    ...entry,
    ...buildAllowedActions(entry.status, entry.presenceStatus, entry.orderStatus),
  }));
}

const CLINICAL_CONTEXT_BY_VISIT_ID: Record<number, DiagnosticOrderClinicalSummaryDto> = {
  1001: {
    visitId: 1001,
    visitCode: 'VIS-202610-1001',
    visitStatus: 'WAITING_DIAGNOSTIC',
    reasonForVisit: 'Đau tức ngực trái lan lên vai, kèm mệt nhiều sau gắng sức.',
    presentingComplaint: 'Đau thắt ngực từng cơn 2 ngày nay, khát nước nhiều, tiểu nhiều.',
    clinicalFindings: 'HA 155/95 mmHg, Mạch 96 lần/phút, SpO2 97%, nhịp tim đều.',
    primaryDiagnosisCode: 'E11.65',
    primaryDiagnosisName:
      'Đái tháo đường không phụ thuộc insuline có tăng đường huyết chưa kiểm soát',
    clinicalDiagnosis: 'Theo dõi hội chứng vành cấp trên nền Đái tháo đường type 2',
  },
  1002: {
    visitId: 1002,
    visitCode: 'VIS-202610-1002',
    visitStatus: 'WAITING_DIAGNOSTIC',
    reasonForVisit: 'Sốt nhẹ về chiều, mệt mỏi, da xanh xao 1 tuần.',
    presentingComplaint: 'Hoa mắt chóng mặt khi thay đổi tư thế.',
    clinicalFindings: 'Niêm mạc nhạt màu, HA 115/75 mmHg, Mạch 84 lần/phút.',
    primaryDiagnosisCode: 'D64.9',
    primaryDiagnosisName: 'Thiếu máu không đặc hiệu',
    clinicalDiagnosis: 'Theo dõi thiếu máu thiếu sắt / rối loạn sinh tủy',
  },
  1003: {
    visitId: 1003,
    visitCode: 'VIS-202610-1003',
    visitStatus: 'WAITING_DIAGNOSTIC',
    reasonForVisit: 'Ho đờm đặc, khó thở khi nằm, sốt 38.5°C ngày thứ 4.',
    presentingComplaint: 'Ho nhiều về đêm, đau tức vùng đáy phổi phải khi hít sâu.',
    clinicalFindings: 'Phổi phải có ran ẩm nhỏ hạt ở đáy phổi, SpO2 96%.',
    primaryDiagnosisCode: 'J18.9',
    primaryDiagnosisName: 'Viêm phổi, không đặc hiệu tác nhân',
    clinicalDiagnosis: 'Theo dõi viêm phổi thùy dưới phổi phải',
  },
  1004: {
    visitId: 1004,
    visitCode: 'VIS-202610-1004',
    visitStatus: 'IN_PROGRESS',
    reasonForVisit: 'Hồi hộp đánh trống ngực, cảm giác hụt nhịp tim.',
    presentingComplaint: 'Cơn tim đập nhanh kịch phát kéo dài 5–10 phút.',
    clinicalFindings: 'Mạch 104 lần/phút, ngoại tâm thu rải rác khi nghe tim.',
    primaryDiagnosisCode: 'I49.9',
    primaryDiagnosisName: 'Loạn nhịp tim, không đặc hiệu',
    clinicalDiagnosis: 'Theo dõi ngoại tâm thu thất / rung nhĩ cơn',
  },
  1005: {
    visitId: 1005,
    visitCode: 'VIS-202610-1005',
    visitStatus: 'WAITING_DIAGNOSTIC',
    reasonForVisit: 'Tiểu buốt, tiểu rắt, đau tức vùng hạ vị 3 ngày.',
    presentingComplaint: 'Nước tiểu đục, không sốt cao.',
    clinicalFindings: 'Ấn đau nhẹ vùng hạ vị, rung thận âm tính 2 bên.',
    primaryDiagnosisCode: 'N30.0',
    primaryDiagnosisName: 'Viêm bàng quang cấp',
    clinicalDiagnosis: 'Nhiễm trùng đường tiết niệu dưới cấp tính',
  },
  1006: {
    visitId: 1006,
    visitCode: 'VIS-202610-1006',
    visitStatus: 'IN_PROGRESS',
    reasonForVisit: 'Đau khớp gối phải khi lên xuống cầu thang, cứng khớp buổi sáng.',
    presentingComplaint: 'Đau tăng khi vận động, giảm khi nghỉ ngơi.',
    clinicalFindings: 'Lạo xạo xương bánh chè (+), không sưng nóng đỏ.',
    primaryDiagnosisCode: 'M17.1',
    primaryDiagnosisName: 'Thoái hóa khớp gối nguyên phát một bên',
    clinicalDiagnosis: 'Thoái hóa khớp gối phải độ II',
  },
};

function createInitialHistory(): Record<number, QueueStatusTransitionHistoryItemDto[]> {
  return {
    9041: [
      {
        auditLogId: 7002,
        queueEntryId: 9041,
        visitId: 1001,
        visitCode: 'VIS-202610-1001',
        previousStatus: 'WAITING',
        targetStatus: 'READY',
        previousPresenceStatus: 'PRESENT',
        updatedPresenceStatus: 'PRESENT',
        previousPriority: 'STAT',
        updatedPriority: 'STAT',
        previousStateVersion: 1,
        updatedStateVersion: 2,
        reason: 'Đã đối chiếu căn cước và sẵn sàng lấy mẫu cấp cứu STAT.',
        blockedReason: null,
        notes: 'Ưu tiên bàn lấy mẫu số 1.',
        updatedByUserId: 304,
        updatedByStaffCode: 'DIAG-009',
        updatedByName: 'CN. Lê Hoàng Minh',
        updatedAtUtc: '2026-10-20T08:04:00Z',
      },
    ],
    9043: [
      {
        auditLogId: 7005,
        queueEntryId: 9043,
        visitId: 1003,
        visitCode: 'VIS-202610-1003',
        previousStatus: 'READY',
        targetStatus: 'CALLED',
        previousPresenceStatus: 'PRESENT',
        updatedPresenceStatus: 'PRESENT',
        previousPriority: 'URGENT',
        updatedPriority: 'URGENT',
        previousStateVersion: 2,
        updatedStateVersion: 3,
        reason: 'Gọi bệnh nhân tiếp theo (C020) vào Buồng chụp X-quang & CT-Scanner 04.',
        blockedReason: null,
        notes: null,
        updatedByUserId: 304,
        updatedByStaffCode: 'DIAG-009',
        updatedByName: 'CN. Lê Hoàng Minh',
        updatedAtUtc: '2026-10-20T08:15:00Z',
      },
    ],
    9045: [
      {
        auditLogId: 7008,
        queueEntryId: 9045,
        visitId: 1005,
        visitCode: 'VIS-202610-1005',
        previousStatus: 'CALLED',
        targetStatus: 'SKIPPED',
        previousPresenceStatus: 'PRESENT',
        updatedPresenceStatus: 'LEFT',
        previousPriority: 'NORMAL',
        updatedPriority: 'NORMAL',
        previousStateVersion: 2,
        updatedStateVersion: 3,
        reason: 'Bệnh nhân vắng mặt khi gọi tên 3 lần tại loa phòng cận lâm sàng.',
        blockedReason: 'Bệnh nhân vắng mặt khi gọi tên 3 lần tại loa phòng cận lâm sàng.',
        notes: 'Chờ bệnh nhân quay lại quầy tiếp đón CLS.',
        updatedByUserId: 304,
        updatedByStaffCode: 'DIAG-009',
        updatedByName: 'CN. Lê Hoàng Minh',
        updatedAtUtc: '2026-10-20T07:58:00Z',
      },
    ],
  };
}

let mockEntriesState: DiagnosticQueueEntryItemDto[] = createInitialEntries();
let mockHistoryState: Record<number, QueueStatusTransitionHistoryItemDto[]> =
  createInitialHistory();
let nextAuditId = 8000;

export function resetDiagnosticQueueMockState(): void {
  mockEntriesState = createInitialEntries();
  mockHistoryState = createInitialHistory();
  nextAuditId = 8000;
}

function cloneEntry(entry: DiagnosticQueueEntryItemDto): DiagnosticQueueEntryItemDto {
  return {
    ...entry,
    allowedTargetStatuses: [...entry.allowedTargetStatuses],
    allowedActions: entry.allowedActions.map((a) => ({ ...a })),
  };
}

function buildServicePointBuckets(
  entries: DiagnosticQueueEntryItemDto[],
): DiagnosticServicePointQueueBucketDto[] {
  return DIAGNOSTIC_QUEUE_SERVICE_POINT_OPTIONS_MOCK.map((sp) => {
    const formattedId = `SP-${sp.servicePointId}`;
    const pointEntries = entries
      .filter(
        (e) =>
          e.servicePointId === sp.servicePointId ||
          e.formattedServicePointId.toUpperCase() === formattedId.toUpperCase(),
      )
      .map(cloneEntry);

    const currentServingEntry =
      pointEntries.find((e) => e.status === 'IN_PROGRESS') ??
      pointEntries.find((e) => e.status === 'CALLED') ??
      null;

    const nextWaitingEntry =
      pointEntries.find((e) => e.isEligible && (e.status === 'READY' || e.status === 'WAITING')) ??
      null;

    const waitingCount = pointEntries.filter(
      (e) => e.status === 'WAITING' || e.status === 'RESERVED',
    ).length;
    const readyCount = pointEntries.filter((e) => e.status === 'READY').length;
    const calledCount = pointEntries.filter((e) => e.status === 'CALLED').length;
    const inProgressCount = pointEntries.filter((e) => e.status === 'IN_PROGRESS').length;
    const completedCount = pointEntries.filter((e) => e.status === 'COMPLETED').length;
    const skippedCount = pointEntries.filter((e) => e.status === 'SKIPPED').length;
    const cancelledCount = pointEntries.filter((e) => e.status === 'CANCELLED').length;

    const category =
      sp.servicePointId === 201
        ? 'LABORATORY'
        : sp.servicePointId === 204
          ? 'IMAGING'
          : 'FUNCTIONAL';
    const categoryLabel =
      category === 'LABORATORY'
        ? 'Xét nghiệm'
        : category === 'IMAGING'
          ? 'Chẩn đoán hình ảnh'
          : 'Thăm dò chức năng';

    return {
      servicePointId: sp.servicePointId,
      formattedServicePointId: formattedId,
      servicePointName: sp.name,
      roomId: sp.roomId ?? null,
      roomCode: sp.roomCode ?? '',
      roomName: sp.roomName ?? '',
      roomLocation: sp.roomLocation ?? null,
      departmentId: 12,
      departmentCode: 'KHOA-CLS',
      departmentName: 'Khoa Xét nghiệm & Chẩn đoán hình ảnh',
      serviceId: sp.serviceId ?? null,
      serviceCode: null,
      serviceName: null,
      category,
      categoryLabel,
      isActive: sp.isActive,
      isRoomActive: sp.isRoomActive,
      capacity: 1,
      currentServingTicketNo: currentServingEntry?.ticketNo ?? null,
      currentServingEntry,
      nextWaitingTicketNo: nextWaitingEntry?.ticketNo ?? null,
      nextWaitingEntry,
      waitingCount,
      readyCount,
      calledCount,
      inProgressCount,
      completedCount,
      skippedCount,
      cancelledCount,
      totalEntriesCount: pointEntries.length,
      estimatedWaitMinutes: (waitingCount + readyCount) * 8,
      entries: pointEntries,
    };
  });
}

function buildSummaryStats(
  entries: DiagnosticQueueEntryItemDto[],
  buckets: DiagnosticServicePointQueueBucketDto[],
): DiagnosticQueueSummaryStatsDto {
  return {
    totalEntries: entries.length,
    waitingCount: entries.filter((e) => e.status === 'WAITING' || e.status === 'RESERVED').length,
    readyCount: entries.filter((e) => e.status === 'READY').length,
    calledCount: entries.filter((e) => e.status === 'CALLED').length,
    inProgressCount: entries.filter((e) => e.status === 'IN_PROGRESS').length,
    completedCount: entries.filter((e) => e.status === 'COMPLETED').length,
    skippedCount: entries.filter((e) => e.status === 'SKIPPED').length,
    cancelledCount: entries.filter((e) => e.status === 'CANCELLED').length,
    urgentOrStatCount: entries.filter(
      (e) =>
        (e.priority === 'STAT' ||
          e.priority === 'URGENT' ||
          e.clinicalPriority === 'STAT' ||
          e.clinicalPriority === 'URGENT') &&
        e.status !== 'COMPLETED' &&
        e.status !== 'CANCELLED',
    ).length,
    mandatoryCount: entries.filter(
      (e) => e.requiredForConclusion && e.status !== 'COMPLETED' && e.status !== 'CANCELLED',
    ).length,
    activeServicePointsCount: buckets.filter((b) => b.isActive && b.isRoomActive).length,
    busyServicePointsCount: buckets.filter((b) => b.calledCount > 0 || b.inProgressCount > 0)
      .length,
    laboratoryCount: entries.filter((e) => e.category === 'LABORATORY').length,
    imagingCount: entries.filter((e) => e.category === 'IMAGING').length,
    functionalCount: entries.filter((e) => e.category === 'FUNCTIONAL').length,
  };
}

function findMockEntry(idOrCode: string): DiagnosticQueueEntryItemDto | undefined {
  const normalized = idOrCode.trim().replace(/^#/, '').toUpperCase();
  return mockEntriesState.find(
    (entry) =>
      entry.id.toUpperCase() === normalized ||
      entry.formattedQueueEntryId.toUpperCase() === normalized ||
      String(entry.queueEntryId) === normalized ||
      entry.ticketNo.toUpperCase() === normalized ||
      (entry.orderCode && entry.orderCode.toUpperCase() === normalized) ||
      (entry.orderId && String(entry.orderId) === normalized),
  );
}

export function resolveMockDiagnosticQueueBoard(
  params?: GetDiagnosticQueueParams,
): DiagnosticQueueBoardResponseDto {
  const allBuckets = buildServicePointBuckets(mockEntriesState);
  const stats = buildSummaryStats(mockEntriesState, allBuckets);

  let filteredEntries = mockEntriesState.map(cloneEntry);
  let filteredBuckets = allBuckets;

  if (params?.servicePointId && params.servicePointId.toUpperCase() !== 'ALL') {
    const spKey = params.servicePointId.trim().toUpperCase();
    filteredEntries = filteredEntries.filter(
      (e) =>
        e.formattedServicePointId.toUpperCase() === spKey ||
        String(e.servicePointId) === spKey ||
        e.roomCode.toUpperCase() === spKey,
    );
    filteredBuckets = allBuckets.filter(
      (b) =>
        b.formattedServicePointId.toUpperCase() === spKey ||
        String(b.servicePointId) === spKey ||
        b.roomCode.toUpperCase() === spKey,
    );
  }

  if (params?.category && params.category.toUpperCase() !== 'ALL') {
    const cat = params.category.trim().toUpperCase();
    filteredEntries = filteredEntries.filter((e) => e.category.toUpperCase() === cat);
  }

  const rawQueueStatus = params?.queueStatus ?? params?.status;
  if (rawQueueStatus && rawQueueStatus.toUpperCase() !== 'ALL') {
    const st = rawQueueStatus.trim().toUpperCase();
    filteredEntries = filteredEntries.filter((e) => e.status.toUpperCase() === st);
  }

  if (params?.priority && params.priority.toUpperCase() !== 'ALL') {
    const prio = params.priority.trim().toUpperCase();
    filteredEntries = filteredEntries.filter(
      (e) => e.priority.toUpperCase() === prio || e.clinicalPriority.toUpperCase() === prio,
    );
  }

  if (params?.presenceStatus && params.presenceStatus.toUpperCase() !== 'ALL') {
    const pres = params.presenceStatus.trim().toUpperCase();
    filteredEntries = filteredEntries.filter((e) => e.presenceStatus.toUpperCase() === pres);
  }

  if (params?.activeOnly) {
    filteredEntries = filteredEntries.filter(
      (e) => e.status !== 'COMPLETED' && e.status !== 'CANCELLED',
    );
  }

  if (params?.requiredForConclusion !== undefined) {
    filteredEntries = filteredEntries.filter(
      (e) => e.requiredForConclusion === params.requiredForConclusion,
    );
  }

  if (params?.keyword?.trim()) {
    const kw = params.keyword.trim().toLowerCase();
    filteredEntries = filteredEntries.filter((e) => {
      const haystack = [
        e.formattedQueueEntryId,
        e.ticketNo,
        e.orderCode ?? '',
        e.visitCode,
        e.patientCode,
        e.patientName,
        e.serviceCode,
        e.serviceName,
        e.roomCode,
        e.roomName,
        e.servicePointName,
        e.orderingDoctorName,
      ]
        .join(' ')
        .toLowerCase();
      return haystack.includes(kw);
    });
  }

  if (params?.limit && params.limit > 0) {
    filteredEntries = filteredEntries.slice(0, params.limit);
  }

  return {
    hospitalId: DIAGNOSTIC_QUEUE_STAFF_PROFILE_MOCK.hospitalId,
    hospitalCode: DIAGNOSTIC_QUEUE_STAFF_PROFILE_MOCK.hospitalCode,
    hospitalName: DIAGNOSTIC_QUEUE_STAFF_PROFILE_MOCK.hospitalName,
    facilityName: DIAGNOSTIC_QUEUE_STAFF_PROFILE_MOCK.hospitalName,
    facilityCode: DIAGNOSTIC_QUEUE_STAFF_PROFILE_MOCK.hospitalCode,
    departmentId: DIAGNOSTIC_QUEUE_STAFF_PROFILE_MOCK.departmentId ?? 12,
    departmentName: DIAGNOSTIC_QUEUE_STAFF_PROFILE_MOCK.departmentName,
    queueDate: params?.queueDate?.trim() || '2026-10-20',
    shiftLabel: 'Ca sáng (07:00 - 11:30)',
    generatedAtUtc: new Date().toISOString(),
    staffProfile: { ...DIAGNOSTIC_QUEUE_STAFF_PROFILE_MOCK },
    stats,
    availableServicePoints: DIAGNOSTIC_QUEUE_SERVICE_POINT_OPTIONS_MOCK.map((sp) => ({ ...sp })),
    servicePoints: filteredBuckets,
    entries: filteredEntries,
    items: filteredEntries,
    quickActions: DIAGNOSTIC_QUICK_ACTIONS_MOCK.map((qa) => ({ ...qa })),
  };
}

export function resolveMockDiagnosticQueueEntryDetail(
  idOrCode: string,
): DiagnosticQueueEntryDetailResponseDto {
  const matched = findMockEntry(idOrCode);
  if (!matched) {
    throw new Error(`Không tìm thấy lượt xếp hàng cận lâm sàng '${idOrCode}'.`);
  }

  const clinicalContext = CLINICAL_CONTEXT_BY_VISIT_ID[matched.visitId] ?? {
    visitId: matched.visitId,
    visitCode: matched.visitCode,
    visitStatus: matched.visitStatus,
    reasonForVisit: 'Khám chuyên khoa và kiểm tra cận lâm sàng theo chỉ định bác sĩ.',
    presentingComplaint: 'Theo dõi triệu chứng lâm sàng tại phòng khám.',
    clinicalFindings: 'Sinh hiệu ổn định, đủ điều kiện thực hiện kỹ thuật cận lâm sàng.',
    primaryDiagnosisCode: 'Z00.0',
    primaryDiagnosisName: 'Khám tổng quát và kiểm tra cận lâm sàng',
    clinicalDiagnosis: 'Theo dõi cận lâm sàng thường quy',
  };

  const history = (mockHistoryState[matched.queueEntryId] ?? []).map((item) => ({ ...item }));

  return {
    hospitalId: DIAGNOSTIC_QUEUE_STAFF_PROFILE_MOCK.hospitalId,
    hospitalCode: DIAGNOSTIC_QUEUE_STAFF_PROFILE_MOCK.hospitalCode,
    hospitalName: DIAGNOSTIC_QUEUE_STAFF_PROFILE_MOCK.hospitalName,
    generatedAtUtc: new Date().toISOString(),
    staffProfile: { ...DIAGNOSTIC_QUEUE_STAFF_PROFILE_MOCK },
    entry: cloneEntry(matched),
    clinicalContext: { ...clinicalContext },
    availableServicePoints: DIAGNOSTIC_QUEUE_SERVICE_POINT_OPTIONS_MOCK.map((sp) => ({ ...sp })),
    statusHistory: history,
  };
}

function applyMockEntryTransition(
  target: DiagnosticQueueEntryItemDto,
  targetStatus: string,
  options: {
    presenceStatus?: string | null;
    servicePointId?: number | null;
    reason?: string | null;
    blockedReason?: string | null;
    notes?: string | null;
    cancelLinkedOrder?: boolean;
    expectedStateVersion?: number | null;
    actionCode: string;
    actionOutcome: string;
  },
): ManageDiagnosticQueueActionResponseDto {
  const normalizedTarget = targetStatus.trim().toUpperCase();
  const previousStatus = target.status;
  const previousOrderStatus = target.orderStatus;
  const previousPresenceStatus = target.presenceStatus;
  const previousServingOrder = target.servingOrder;
  const previousStateVersion = target.stateVersion;

  if (
    options.expectedStateVersion !== undefined &&
    options.expectedStateVersion !== null &&
    options.expectedStateVersion !== target.stateVersion
  ) {
    throw new Error(
      `Dữ liệu lượt xếp hàng '${target.ticketNo}' đã thay đổi (phiên bản hiện tại v${target.stateVersion}, yêu cầu v${options.expectedStateVersion}). Vui lòng tải lại hàng đợi.`,
    );
  }

  const allowedTargets = ALLOWED_TRANSITIONS[previousStatus] ?? [];
  if (previousStatus !== normalizedTarget && !allowedTargets.includes(normalizedTarget)) {
    throw new Error(
      `Không thể chuyển trạng thái hàng đợi cận lâm sàng từ '${previousStatus}' sang '${normalizedTarget}'.`,
    );
  }

  const nowIso = new Date().toISOString();
  const nextPresence =
    options.presenceStatus?.trim().toUpperCase() ||
    (normalizedTarget === 'SKIPPED' || normalizedTarget === 'CANCELLED' ? 'LEFT' : 'PRESENT');

  if (options.servicePointId) {
    const chosenSp = DIAGNOSTIC_QUEUE_SERVICE_POINT_OPTIONS_MOCK.find(
      (sp) => sp.servicePointId === options.servicePointId,
    );
    if (chosenSp) {
      target.servicePointId = chosenSp.servicePointId;
      target.formattedServicePointId = `SP-${chosenSp.servicePointId}`;
      target.servicePointName = chosenSp.name;
      target.roomId = chosenSp.roomId ?? target.roomId;
      target.roomCode = chosenSp.roomCode ?? target.roomCode;
      target.roomName = chosenSp.roomName ?? target.roomName;
      target.roomLocation = chosenSp.roomLocation ?? target.roomLocation;
    }
  }

  target.status = normalizedTarget;
  target.statusLabel = STATUS_LABELS[normalizedTarget] ?? normalizedTarget;
  target.presenceStatus = nextPresence;
  target.presenceStatusLabel = PRESENCE_LABELS[nextPresence] ?? nextPresence;
  target.stateVersion = previousStateVersion + 1;

  if (normalizedTarget === 'READY') {
    target.readyAt = nowIso;
    target.blockedReason = null;
    target.estimatedWaitMinutes = 0;
    target.estimatedWaitLabel = 'Sẵn sàng gọi';
  } else if (normalizedTarget === 'CALLED') {
    target.calledAt = nowIso;
    target.blockedReason = null;
    target.estimatedWaitMinutes = 0;
    target.estimatedWaitLabel = 'Đang gọi vào phòng';
  } else if (normalizedTarget === 'IN_PROGRESS') {
    target.startedAt = nowIso;
    target.orderStatus = 'IN_PROGRESS';
    target.orderStatusLabel = 'Đang thực hiện';
    target.blockedReason = null;
    target.estimatedWaitMinutes = 0;
    target.estimatedWaitLabel = 'Đang thực hiện kỹ thuật';
  } else if (normalizedTarget === 'COMPLETED') {
    target.completedAt = nowIso;
    target.orderStatus = 'COMPLETED';
    target.orderStatusLabel = 'Đã hoàn tất';
    target.blockedReason = null;
    target.estimatedWaitMinutes = 0;
    target.estimatedWaitLabel = 'Đã hoàn tất';
  } else if (normalizedTarget === 'SKIPPED') {
    target.blockedReason =
      options.blockedReason?.trim() ||
      options.reason?.trim() ||
      'Bệnh nhân vắng mặt khi gọi tên tại phòng cận lâm sàng.';
    target.estimatedWaitMinutes = 15;
    target.estimatedWaitLabel = 'Chờ khôi phục lượt';
  } else if (normalizedTarget === 'CANCELLED') {
    target.blockedReason =
      options.blockedReason?.trim() || options.reason?.trim() || 'Đã hủy lượt xếp hàng.';
    if (options.cancelLinkedOrder) {
      target.orderStatus = 'CANCELLED';
      target.orderStatusLabel = 'Đã hủy';
    }
    target.estimatedWaitMinutes = 0;
    target.estimatedWaitLabel = 'Đã hủy lượt';
  }

  const rebuiltFlags = buildAllowedActions(
    target.status,
    target.presenceStatus,
    target.orderStatus,
  );
  Object.assign(target, rebuiltFlags);

  const historyEntry: QueueStatusTransitionHistoryItemDto = {
    auditLogId: ++nextAuditId,
    queueEntryId: target.queueEntryId,
    visitId: target.visitId,
    visitCode: target.visitCode,
    previousStatus,
    targetStatus: normalizedTarget,
    previousPresenceStatus,
    updatedPresenceStatus: target.presenceStatus,
    previousPriority: target.clinicalPriority,
    updatedPriority: target.clinicalPriority,
    previousStateVersion,
    updatedStateVersion: target.stateVersion,
    reason: options.reason ?? target.blockedReason ?? null,
    blockedReason: target.blockedReason ?? null,
    notes: options.notes ?? null,
    updatedByUserId: DIAGNOSTIC_QUEUE_STAFF_PROFILE_MOCK.userId,
    updatedByStaffCode: DIAGNOSTIC_QUEUE_STAFF_PROFILE_MOCK.staffCode ?? 'DIAG-009',
    updatedByName: DIAGNOSTIC_QUEUE_STAFF_PROFILE_MOCK.fullName,
    updatedAtUtc: nowIso,
  };

  const existingHistory = mockHistoryState[target.queueEntryId] ?? [];
  mockHistoryState[target.queueEntryId] = [historyEntry, ...existingHistory];

  const allBuckets = buildServicePointBuckets(mockEntriesState);
  const servicePointBucket =
    allBuckets.find((b) => b.servicePointId === target.servicePointId) ?? null;

  const message = `Đã cập nhật lượt xếp hàng ${target.ticketNo} (${target.patientName}) từ ${previousStatus} sang ${normalizedTarget} (v${target.stateVersion}).`;

  return {
    actionOutcome: options.actionOutcome,
    action: options.actionCode,
    message,
    queueEntryId: target.queueEntryId,
    formattedQueueEntryId: target.formattedQueueEntryId,
    ticketNo: target.ticketNo,
    queueNumber: target.queueNumber,
    orderId: target.orderId ?? null,
    orderCode: target.orderCode ?? null,
    previousStatus,
    currentStatus: target.status,
    previousOrderStatus,
    orderStatus: target.orderStatus,
    previousPresenceStatus,
    presenceStatus: target.presenceStatus,
    previousServingOrder,
    servingOrder: target.servingOrder,
    previousStateVersion,
    stateVersion: target.stateVersion,
    blockedReason: target.blockedReason ?? null,
    reason: options.reason ?? null,
    notes: options.notes ?? null,
    updatedAtUtc: nowIso,
    updatedByStaff: { ...DIAGNOSTIC_QUEUE_STAFF_PROFILE_MOCK },
    entry: cloneEntry(target),
    servicePointBucket,
  };
}

export function callNextMockDiagnosticPatient(
  servicePointId: string,
  payload?: CallNextDiagnosticPatientRequest,
): CallNextDiagnosticPatientResponseDto {
  const effectiveSpKey = (servicePointId || payload?.servicePointId || '').trim().toUpperCase();
  const spOption = DIAGNOSTIC_QUEUE_SERVICE_POINT_OPTIONS_MOCK.find(
    (sp) =>
      `SP-${sp.servicePointId}`.toUpperCase() === effectiveSpKey ||
      String(sp.servicePointId) === effectiveSpKey ||
      (sp.roomCode && sp.roomCode.toUpperCase() === effectiveSpKey),
  );

  if (!spOption) {
    throw new Error(`Không tìm thấy điểm phục vụ cận lâm sàng '${servicePointId}'.`);
  }

  const allBuckets = buildServicePointBuckets(mockEntriesState);
  const bucket = allBuckets.find((b) => b.servicePointId === spOption.servicePointId)!;

  const busyEntry = mockEntriesState.find(
    (e) =>
      e.servicePointId === spOption.servicePointId &&
      (e.status === 'CALLED' || e.status === 'IN_PROGRESS'),
  );

  if (busyEntry) {
    throw new Error(
      `Điểm phục vụ cận lâm sàng đang có bệnh nhân được gọi hoặc đang thực hiện kỹ thuật (${busyEntry.ticketNo} • ${busyEntry.patientName}). Hoàn tất hoặc đánh dấu qua lượt trước khi gọi bệnh nhân tiếp theo.`,
    );
  }

  const candidate = mockEntriesState.find(
    (e) =>
      e.servicePointId === spOption.servicePointId &&
      e.isEligible &&
      (e.status === 'READY' || e.status === 'WAITING') &&
      (!payload?.category ||
        payload.category === 'ALL' ||
        e.category.toUpperCase() === payload.category.trim().toUpperCase()),
  );

  if (!candidate) {
    return {
      servicePointId: spOption.servicePointId,
      formattedServicePointId: bucket.formattedServicePointId,
      servicePointName: bucket.servicePointName,
      outcome: 'NO_ELIGIBLE_PATIENT',
      message: `Không có bệnh nhân đủ điều kiện chờ gọi tại ${bucket.servicePointName}.`,
      selectionReason: null,
      calledEntry: null,
      previousStateVersion: null,
      stateVersion: null,
      updatedAtUtc: new Date().toISOString(),
      calledByStaff: { ...DIAGNOSTIC_QUEUE_STAFF_PROFILE_MOCK },
      servicePointBucket: bucket,
      entries: bucket.entries,
    };
  }

  const selectionReason =
    candidate.priority === 'STAT' || candidate.priority === 'URGENT'
      ? `PRIORITY_${candidate.priority}`
      : 'FIFO_READY';

  const transition = applyMockEntryTransition(candidate, 'CALLED', {
    presenceStatus: 'PRESENT',
    reason:
      payload?.notes?.trim() ||
      `Gọi bệnh nhân tiếp theo (${candidate.ticketNo}) vào ${bucket.servicePointName}.`,
    notes: payload?.notes ?? null,
    expectedStateVersion: payload?.expectedStateVersion,
    actionCode: 'DIAGNOSTIC_QUEUE_CALL_NEXT',
    actionOutcome: 'CALLED',
  });

  const updatedBucket =
    transition.servicePointBucket ??
    buildServicePointBuckets(mockEntriesState).find(
      (b) => b.servicePointId === spOption.servicePointId,
    )!;

  return {
    servicePointId: spOption.servicePointId,
    formattedServicePointId: updatedBucket.formattedServicePointId,
    servicePointName: updatedBucket.servicePointName,
    outcome: 'CALLED',
    message: `Đã gọi bệnh nhân ${transition.entry.patientName} (${transition.entry.ticketNo}) vào ${updatedBucket.servicePointName}.`,
    selectionReason,
    calledEntry: transition.entry,
    previousStateVersion: transition.previousStateVersion,
    stateVersion: transition.stateVersion,
    updatedAtUtc: transition.updatedAtUtc,
    calledByStaff: { ...DIAGNOSTIC_QUEUE_STAFF_PROFILE_MOCK },
    servicePointBucket: updatedBucket,
    entries: updatedBucket.entries,
  };
}

export function updateMockDiagnosticQueueStatus(
  idOrCode: string,
  payload: UpdateDiagnosticQueueStatusRequest,
): ManageDiagnosticQueueActionResponseDto {
  const matched = findMockEntry(idOrCode);
  if (!matched) {
    throw new Error(`Không tìm thấy lượt xếp hàng cận lâm sàng '${idOrCode}'.`);
  }

  const targetStatus = (payload.targetStatus || payload.status || '').trim().toUpperCase();
  if (!targetStatus) {
    throw new Error('Trạng thái hàng đợi cận lâm sàng đích (TargetStatus) không được để trống.');
  }

  return applyMockEntryTransition(matched, targetStatus, {
    presenceStatus: payload.presenceStatus,
    servicePointId: payload.servicePointId,
    reason: payload.reason ?? payload.blockedReason,
    blockedReason: payload.blockedReason ?? payload.reason,
    notes: payload.notes,
    cancelLinkedOrder: Boolean(payload.cancelLinkedOrder),
    expectedStateVersion: payload.expectedStateVersion,
    actionCode: 'DIAGNOSTIC_QUEUE_STATUS_UPDATED',
    actionOutcome: `STATUS_${targetStatus}`,
  });
}

export function markMockDiagnosticMissedTurn(
  idOrCode: string,
  payload?: DiagnosticQueueTurnActionRequest,
): ManageDiagnosticQueueActionResponseDto {
  const matched = findMockEntry(idOrCode);
  if (!matched) {
    throw new Error(`Không tìm thấy lượt xếp hàng cận lâm sàng '${idOrCode}'.`);
  }

  const rawReason = (payload?.blockedReason || payload?.reason || '').trim();
  if (rawReason.length < 5 || rawReason.length > 500) {
    throw new Error('Lý do đánh dấu bệnh nhân qua lượt cận lâm sàng phải từ 5 đến 500 ký tự.');
  }

  return applyMockEntryTransition(matched, 'SKIPPED', {
    presenceStatus: payload?.presenceStatus ?? 'LEFT',
    reason: rawReason,
    blockedReason: rawReason,
    notes: payload?.notes,
    expectedStateVersion: payload?.expectedStateVersion,
    actionCode: 'DIAGNOSTIC_QUEUE_MISSED_TURN',
    actionOutcome: 'MISSED_TURN',
  });
}

export function restoreMockDiagnosticSkippedTurn(
  idOrCode: string,
  payload?: DiagnosticQueueTurnActionRequest,
): ManageDiagnosticQueueActionResponseDto {
  const matched = findMockEntry(idOrCode);
  if (!matched) {
    throw new Error(`Không tìm thấy lượt xếp hàng cận lâm sàng '${idOrCode}'.`);
  }

  if (matched.status !== 'SKIPPED') {
    throw new Error('Chỉ khôi phục lượt cho bệnh nhân đang ở trạng thái Qua lượt (SKIPPED).');
  }

  const effectiveReason =
    payload?.reason?.trim() ||
    'Bệnh nhân đã quay lại phòng cận lâm sàng và được khôi phục lượt chờ.';

  return applyMockEntryTransition(matched, 'READY', {
    presenceStatus: payload?.presenceStatus ?? 'PRESENT',
    reason: effectiveReason,
    blockedReason: null,
    notes: payload?.notes,
    expectedStateVersion: payload?.expectedStateVersion,
    actionCode: 'DIAGNOSTIC_QUEUE_TURN_RESTORED',
    actionOutcome: 'RESTORED_TURN',
  });
}

export function cancelMockDiagnosticQueueEntry(
  idOrCode: string,
  payload: CancelDiagnosticQueueEntryRequest,
): ManageDiagnosticQueueActionResponseDto {
  const matched = findMockEntry(idOrCode);
  if (!matched) {
    throw new Error(`Không tìm thấy lượt xếp hàng cận lâm sàng '${idOrCode}'.`);
  }

  const rawReason = (payload.reason || '').trim();
  if (rawReason.length < 5 || rawReason.length > 500) {
    throw new Error('Lý do hủy lượt xếp hàng cận lâm sàng phải từ 5 đến 500 ký tự.');
  }

  return applyMockEntryTransition(matched, 'CANCELLED', {
    presenceStatus: 'LEFT',
    reason: rawReason,
    blockedReason: rawReason,
    notes: payload.notes,
    cancelLinkedOrder: Boolean(payload.cancelLinkedOrder),
    expectedStateVersion: payload.expectedStateVersion,
    actionCode: 'DIAGNOSTIC_QUEUE_ENTRY_CANCELLED',
    actionOutcome: 'CANCELLED',
  });
}
