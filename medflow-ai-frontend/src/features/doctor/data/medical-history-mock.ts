import type {
  GetPatientMedicalHistoryQueryParams,
  MedicalHistoryLookupMode,
  PatientMedicalHistoryResponseDto,
} from '../types/medical-history.types';

export const DEFAULT_MEDICAL_HISTORY_PATIENT_CODE = 'BN-2026-0212';
export const DEFAULT_MEDICAL_HISTORY_VISIT_CODE = 'VIS-2026-0212';

export const PATIENT_MEDICAL_HISTORY_MOCK: PatientMedicalHistoryResponseDto = {
  facilityName: 'Bệnh viện Đa khoa Trung ương MedFlow',
  facilityCode: 'MEDFLOW-HN',
  generatedAtUtc: '2026-10-20T09:15:00Z',
  patient: {
    patientId: 212,
    patientCode: DEFAULT_MEDICAL_HISTORY_PATIENT_CODE,
    fullName: 'Nguyễn Văn An',
    gender: 'MALE',
    dateOfBirth: '1965-04-12',
    age: 61,
    bloodType: 'O+',
    allergiesSummary: 'Dị ứng Penicillin (phát ban mày đay năm 2021)',
    chronicConditions: ['Tăng huyết áp độ 2 (I10)', 'Rối loạn chuyển hóa lipoprotein (E78)'],
  },
  currentVisit: {
    visitId: 9012,
    visitCode: DEFAULT_MEDICAL_HISTORY_VISIT_CODE,
    status: 'IN_PROGRESS',
    reasonForVisit: 'Đau tức ngực trái lan lên vai, huyết áp sáng đo tại nhà 150/92 mmHg',
    checkedInAt: '2026-10-20T08:10:00Z',
    roomCode: 'PK-101',
    roomName: 'Phòng khám Nội Tim Mạch 101',
    departmentName: 'Khoa Nội Tim Mạch',
    doctorName: 'BS. CKI Nguyễn Hoàng Nam',
  },
  stats: {
    totalVisits: 3,
    completedVisits: 2,
    finalizedRecordsCount: 2,
    totalDiagnosesCount: 5,
    totalDiagnosticOrdersCount: 3,
    totalPrescriptionsCount: 3,
    lastVisitAt: '2026-08-14T10:30:00Z',
  },
  visits: [
    {
      visitId: 9012,
      visitCode: DEFAULT_MEDICAL_HISTORY_VISIT_CODE,
      visitDate: '2026-10-20',
      checkedInAt: '2026-10-20T08:10:00Z',
      completedAt: null,
      status: 'IN_PROGRESS',
      visitType: 'SCHEDULED',
      reasonForVisit: 'Đau tức ngực trái lan lên vai, huyết áp sáng đo tại nhà 150/92 mmHg',
      departmentId: 10,
      departmentName: 'Khoa Nội Tim Mạch',
      specialtyCode: 'CARDIOLOGY',
      specialtyName: 'Nội Tim Mạch',
      roomCode: 'PK-101',
      roomName: 'Phòng khám Nội Tim Mạch 101',
      doctorId: 101,
      doctorCode: 'DOC-0101',
      doctorName: 'BS. CKI Nguyễn Hoàng Nam',
      isAssignedToCurrentDoctor: true,
      isCurrentVisit: true,
      medicalRecord: {
        medicalRecordId: 5012,
        status: 'DRAFT',
        finalizedAt: null,
        latestRevisionNo: 1,
        isConfirmed: false,
        recordedAt: '2026-10-20T08:35:00Z',
        editedByDoctorName: 'BS. CKI Nguyễn Hoàng Nam',
        presentingComplaint: 'Đau tức ngực trái âm ỉ 2 ngày nay, tăng khi gắng sức nhẹ.',
        symptomHistory:
          'Bệnh nhân tuân thủ thuốc huyết áp nhưng 3 ngày gần đây ngủ kém, xuất hiện cơn tức ngực trái kéo dài 5-10 phút.',
        relevantHistoryAllergies: 'Dị ứng Penicillin; tiền sử tăng huyết áp 5 năm.',
        clinicalFindings:
          'Tỉnh, tiếp xúc tốt. Tim nhịp đều 84 chu kỳ/phút, T1 T2 rõ, không nghe âm thổi bệnh lý. Phổi thông khí đều hai bên.',
        clinicalConclusion:
          'Theo dõi cơn đau thắt ngực ổn định trên nền Tăng huyết áp độ 2. Chờ đối chiếu thêm siêu âm tim.',
        amendmentReason: null,
        vitalSigns: {
          bpSystolicMmhg: 145,
          bpDiastolicMmhg: 90,
          bloodPressureLabel: '145/90 mmHg',
          pulseBpm: 84,
          temperatureC: 36.8,
          spo2Percent: 98,
          heightCm: 168,
          weightKg: 68.3,
          bmi: 24.2,
        },
        revisionCount: 1,
        revisions: [
          {
            revisionId: 7101,
            revisionNo: 1,
            recordedAt: '2026-10-20T08:35:00Z',
            editedByDoctorName: 'BS. CKI Nguyễn Hoàng Nam',
            isConfirmed: false,
            amendmentReason: 'Khởi tạo hồ sơ khám ban đầu',
            clinicalConclusion:
              'Theo dõi cơn đau thắt ngực ổn định trên nền Tăng huyết áp độ 2. Chờ đối chiếu thêm siêu âm tim.',
          },
        ],
        addenda: [],
      },
      diagnoses: [
        {
          diagnosisId: 301,
          icd10CodeId: 1001,
          icd10Code: 'I10',
          icd10NameVi: 'Tăng huyết áp vô căn (nguyên phát)',
          catalogVersion: 'ICD-10-VN-2024',
          clinicalDiagnosis: 'Tăng huyết áp độ 2 đang điều trị ngoại trú',
          isPrimary: true,
          status: 'CONFIRMED',
          confirmedAt: '2026-10-20T08:40:00Z',
          recordedAt: '2026-10-20T08:40:00Z',
          recordedByDoctorName: 'BS. CKI Nguyễn Hoàng Nam',
        },
        {
          diagnosisId: 302,
          icd10CodeId: 1002,
          icd10Code: 'I20.9',
          icd10NameVi: 'Cơn đau thắt ngực, không đặc hiệu',
          catalogVersion: 'ICD-10-VN-2024',
          clinicalDiagnosis: 'Theo dõi đau thắt ngực ổn định',
          isPrimary: false,
          status: 'PRELIMINARY',
          confirmedAt: null,
          recordedAt: '2026-10-20T08:42:00Z',
          recordedByDoctorName: 'BS. CKI Nguyễn Hoàng Nam',
        },
      ],
      diagnosticOrders: [
        {
          orderId: 921,
          orderCode: 'CLS-0921',
          serviceId: 401,
          serviceCode: 'ECG-12',
          serviceName: 'Điện tâm đồ 12 chuyển đạo thường quy',
          priority: 'URGENT',
          status: 'COMPLETED',
          requiredForConclusion: true,
          instruction: 'Đo điện tim nghỉ ngay tại phòng chức năng tầng 2',
          orderedAt: '2026-10-20T08:25:00Z',
          confirmedAt: '2026-10-20T08:45:00Z',
          resultStatus: 'FINALIZED',
          resultFinalizedAt: '2026-10-20T08:50:00Z',
          resultVersionNo: 1,
          resultText:
            'Nhịp xoang đều 82 ck/phút. Trục trung gian. ST chênh xuống nhẹ 0.5mm ở V5-V6.',
          conclusion: 'Nhịp xoang đều, theo dõi thiếu máu cơ tim cục bộ thành bên dưới.',
          reportFileUrl: null,
          isResultFinalized: true,
        },
      ],
      prescriptions: [
        {
          prescriptionId: 812,
          prescriptionCode: 'RX-2026-0812',
          status: 'ISSUED',
          fulfillmentChoice: 'HOSPITAL_PHARMACY',
          issuedAt: '2026-10-20T09:00:00Z',
          prescribingDoctorName: 'BS. CKI Nguyễn Hoàng Nam',
          items: [
            {
              itemId: 1801,
              medicineId: 501,
              medicineCode: 'MED-AMLO-5',
              medicineName: 'Amlodipin 5mg',
              activeIngredient: 'Amlodipine besilate',
              strength: '5mg',
              dosageForm: 'Viên nang cứng',
              dispensingUnit: 'Viên',
              prescribedQuantity: 30,
              singleDoseQuantity: 1,
              singleDoseUnit: 'Viên',
              doseScheduleText: 'Sáng 1 viên sau ăn',
              frequency: '1 lần/ngày',
              durationDays: 30,
              administrationRoute: 'Uống',
              instructions: 'Theo dõi huyết áp mỗi sáng, tránh tự ý ngưng thuốc.',
            },
          ],
        },
      ],
    },
    {
      visitId: 8840,
      visitCode: 'VIS-2026-0140',
      visitDate: '2026-08-14',
      checkedInAt: '2026-08-14T09:15:00Z',
      completedAt: '2026-08-14T10:30:00Z',
      status: 'COMPLETED',
      visitType: 'SCHEDULED',
      reasonForVisit: 'Tái khám định kỳ tăng huyết áp và kiểm tra mỡ máu',
      departmentId: 10,
      departmentName: 'Khoa Nội Tim Mạch',
      specialtyCode: 'CARDIOLOGY',
      specialtyName: 'Nội Tim Mạch',
      roomCode: 'PK-101',
      roomName: 'Phòng khám Nội Tim Mạch 101',
      doctorId: 101,
      doctorCode: 'DOC-0101',
      doctorName: 'BS. CKI Nguyễn Hoàng Nam',
      isAssignedToCurrentDoctor: true,
      isCurrentVisit: false,
      medicalRecord: {
        medicalRecordId: 4840,
        status: 'FINALIZED',
        finalizedAt: '2026-08-14T10:25:00Z',
        latestRevisionNo: 2,
        isConfirmed: true,
        recordedAt: '2026-08-14T10:20:00Z',
        editedByDoctorName: 'BS. CKI Nguyễn Hoàng Nam',
        presentingComplaint: 'Tái khám theo hẹn, thỉnh thoảng nặng đầu buổi chiều.',
        symptomHistory: 'Huyết áp dao động 135-142/85 mmHg, không đau ngực, không khó thở.',
        relevantHistoryAllergies: 'Dị ứng Penicillin.',
        clinicalFindings: 'Tim đều 78 ck/phút, phổi trong, bụng mềm.',
        clinicalConclusion:
          'Tăng huyết áp độ 2 đáp ứng một phần, kèm rối loạn lipid máu. Điều chỉnh liều Statin và hẹn tái khám sau 8 tuần.',
        amendmentReason: 'Cập nhật kết quả bilan lipid máu vào kết luận',
        vitalSigns: {
          bpSystolicMmhg: 138,
          bpDiastolicMmhg: 86,
          bloodPressureLabel: '138/86 mmHg',
          pulseBpm: 78,
          temperatureC: 36.6,
          spo2Percent: 99,
          heightCm: 168,
          weightKg: 69.0,
          bmi: 24.4,
        },
        revisionCount: 2,
        revisions: [
          {
            revisionId: 6902,
            revisionNo: 2,
            recordedAt: '2026-08-14T10:20:00Z',
            editedByDoctorName: 'BS. CKI Nguyễn Hoàng Nam',
            isConfirmed: true,
            amendmentReason: 'Cập nhật kết quả bilan lipid máu vào kết luận',
            clinicalConclusion:
              'Tăng huyết áp độ 2 đáp ứng một phần, kèm rối loạn lipid máu. Điều chỉnh liều Statin và hẹn tái khám sau 8 tuần.',
          },
          {
            revisionId: 6901,
            revisionNo: 1,
            recordedAt: '2026-08-14T09:40:00Z',
            editedByDoctorName: 'BS. CKI Nguyễn Hoàng Nam',
            isConfirmed: false,
            amendmentReason: 'Ghi nhận khám lâm sàng ban đầu',
            clinicalConclusion: 'Tăng huyết áp độ 2 đang chờ kết quả xét nghiệm sinh hóa máu.',
          },
        ],
        addenda: [
          {
            addendumId: 401,
            recordType: 'MEDICAL_RECORD',
            targetRecordId: 4840,
            reason: 'Bổ sung tư vấn chế độ ăn giảm muối',
            note: 'Đã hướng dẫn bệnh nhân ăn nhạt (<5g muối/ngày) và ghi nhật ký huyết áp tại nhà.',
            correctedContentJson: null,
            authorDoctorName: 'BS. CKI Nguyễn Hoàng Nam',
            recordedAt: '2026-08-14T10:28:00Z',
          },
        ],
      },
      diagnoses: [
        {
          diagnosisId: 251,
          icd10CodeId: 1001,
          icd10Code: 'I10',
          icd10NameVi: 'Tăng huyết áp vô căn (nguyên phát)',
          catalogVersion: 'ICD-10-VN-2024',
          clinicalDiagnosis: 'Tăng huyết áp nguyên phát độ 2',
          isPrimary: true,
          status: 'CONFIRMED',
          confirmedAt: '2026-08-14T10:22:00Z',
          recordedAt: '2026-08-14T10:22:00Z',
          recordedByDoctorName: 'BS. CKI Nguyễn Hoàng Nam',
        },
        {
          diagnosisId: 252,
          icd10CodeId: 1045,
          icd10Code: 'E78.5',
          icd10NameVi: 'Tăng lipid máu, không đặc hiệu',
          catalogVersion: 'ICD-10-VN-2024',
          clinicalDiagnosis: 'Rối loạn lipid máu hỗn hợp',
          isPrimary: false,
          status: 'CONFIRMED',
          confirmedAt: '2026-08-14T10:22:00Z',
          recordedAt: '2026-08-14T10:22:00Z',
          recordedByDoctorName: 'BS. CKI Nguyễn Hoàng Nam',
        },
      ],
      diagnosticOrders: [
        {
          orderId: 845,
          orderCode: 'CLS-0845',
          serviceId: 305,
          serviceCode: 'LAB-LIPID',
          serviceName: 'Bộ xét nghiệm Mỡ máu (Cholesterol, Triglyceride, HDL-C, LDL-C)',
          priority: 'ROUTINE',
          status: 'COMPLETED',
          requiredForConclusion: true,
          instruction: 'Lấy máu tĩnh mạch lúc đói',
          orderedAt: '2026-08-14T09:30:00Z',
          confirmedAt: '2026-08-14T10:05:00Z',
          resultStatus: 'FINALIZED',
          resultFinalizedAt: '2026-08-14T10:10:00Z',
          resultVersionNo: 1,
          resultText:
            'Cholesterol toàn phần: 5.8 mmol/L; LDL-C: 3.7 mmol/L; Triglyceride: 2.1 mmol/L.',
          conclusion: 'Tăng nhẹ LDL-C và Triglyceride máu.',
          reportFileUrl: null,
          isResultFinalized: true,
        },
      ],
      prescriptions: [
        {
          prescriptionId: 740,
          prescriptionCode: 'RX-2026-0740',
          status: 'DISPENSED',
          fulfillmentChoice: 'HOSPITAL_PHARMACY',
          issuedAt: '2026-08-14T10:25:00Z',
          prescribingDoctorName: 'BS. CKI Nguyễn Hoàng Nam',
          items: [
            {
              itemId: 1620,
              medicineId: 501,
              medicineCode: 'MED-AMLO-5',
              medicineName: 'Amlodipin 5mg',
              activeIngredient: 'Amlodipine besilate',
              strength: '5mg',
              dosageForm: 'Viên nang cứng',
              dispensingUnit: 'Viên',
              prescribedQuantity: 60,
              singleDoseQuantity: 1,
              singleDoseUnit: 'Viên',
              doseScheduleText: 'Sáng 1 viên',
              frequency: '1 lần/ngày',
              durationDays: 60,
              administrationRoute: 'Uống',
              instructions: 'Uống sau ăn sáng.',
            },
            {
              itemId: 1621,
              medicineId: 520,
              medicineCode: 'MED-ATOR-10',
              medicineName: 'Atorvastatin 10mg',
              activeIngredient: 'Atorvastatin calcium',
              strength: '10mg',
              dosageForm: 'Viên nén bao phim',
              dispensingUnit: 'Viên',
              prescribedQuantity: 60,
              singleDoseQuantity: 1,
              singleDoseUnit: 'Viên',
              doseScheduleText: 'Tối 1 viên',
              frequency: '1 lần/ngày',
              durationDays: 60,
              administrationRoute: 'Uống',
              instructions: 'Uống sau bữa tối.',
            },
          ],
        },
      ],
    },
    {
      visitId: 7988,
      visitCode: 'VIS-2025-0988',
      visitDate: '2025-12-05',
      checkedInAt: '2025-12-05T14:00:00Z',
      completedAt: '2025-12-05T15:10:00Z',
      status: 'COMPLETED',
      visitType: 'WALK_IN',
      reasonForVisit: 'Ho khan kéo dài sau nhiễm lạnh, kiểm tra huyết áp',
      departmentId: 12,
      departmentName: 'Khoa Nội Tổng Quát',
      specialtyCode: 'GENERAL_MEDICINE',
      specialtyName: 'Nội Tổng Quát',
      roomCode: 'PK-204',
      roomName: 'Phòng khám Nội Tổng Quát 204',
      doctorId: 108,
      doctorCode: 'DOC-0108',
      doctorName: 'BS. CKI Trần Minh Quân',
      isAssignedToCurrentDoctor: false,
      isCurrentVisit: false,
      medicalRecord: {
        medicalRecordId: 3988,
        status: 'FINALIZED',
        finalizedAt: '2025-12-05T15:05:00Z',
        latestRevisionNo: 1,
        isConfirmed: true,
        recordedAt: '2025-12-05T14:55:00Z',
        editedByDoctorName: 'BS. CKI Trần Minh Quân',
        presentingComplaint: 'Ho khan 5 ngày, không sốt, rát họng nhẹ.',
        symptomHistory: 'Khởi phát sau thay đổi thời tiết, đã súc họng nước muối sinh lý.',
        relevantHistoryAllergies: 'Dị ứng Penicillin — tuyệt đối tránh nhóm Beta-lactam.',
        clinicalFindings: 'Họng sung huyết nhẹ, phổi không ran, nhịp tim đều 76 ck/phút.',
        clinicalConclusion: 'Viêm họng cấp trên bệnh nhân tăng huyết áp ổn định.',
        amendmentReason: null,
        vitalSigns: {
          bpSystolicMmhg: 132,
          bpDiastolicMmhg: 82,
          bloodPressureLabel: '132/82 mmHg',
          pulseBpm: 76,
          temperatureC: 36.9,
          spo2Percent: 99,
          heightCm: 168,
          weightKg: 68.5,
          bmi: 24.3,
        },
        revisionCount: 1,
        revisions: [],
        addenda: [],
      },
      diagnoses: [
        {
          diagnosisId: 190,
          icd10CodeId: 1200,
          icd10Code: 'J02.9',
          icd10NameVi: 'Viêm họng cấp, không đặc hiệu',
          catalogVersion: 'ICD-10-VN-2024',
          clinicalDiagnosis: 'Viêm họng cấp',
          isPrimary: true,
          status: 'CONFIRMED',
          confirmedAt: '2025-12-05T15:00:00Z',
          recordedAt: '2025-12-05T15:00:00Z',
          recordedByDoctorName: 'BS. CKI Trần Minh Quân',
        },
      ],
      diagnosticOrders: [
        {
          orderId: 612,
          orderCode: 'CLS-0612',
          serviceId: 210,
          serviceCode: 'XRAY-CHEST',
          serviceName: 'Chụp X-quang ngực thẳng kỹ thuật số',
          priority: 'ROUTINE',
          status: 'COMPLETED',
          requiredForConclusion: false,
          instruction: 'Loại trừ tổn thương nhu mô phổi',
          orderedAt: '2025-12-05T14:20:00Z',
          confirmedAt: '2025-12-05T14:40:00Z',
          resultStatus: 'FINALIZED',
          resultFinalizedAt: '2025-12-05T14:45:00Z',
          resultVersionNo: 1,
          resultText: 'Trường phổi hai bên sáng đều, bóng tim không to, góc sườn hoành nhọn.',
          conclusion: 'Hình ảnh tim phổi trong giới hạn bình thường.',
          reportFileUrl: null,
          isResultFinalized: true,
        },
      ],
      prescriptions: [
        {
          prescriptionId: 512,
          prescriptionCode: 'RX-2025-0512',
          status: 'DISPENSED',
          fulfillmentChoice: 'EXTERNAL_PHARMACY',
          issuedAt: '2025-12-05T15:05:00Z',
          prescribingDoctorName: 'BS. CKI Trần Minh Quân',
          items: [
            {
              itemId: 1120,
              medicineId: 610,
              medicineCode: 'MED-DEXTRO-15',
              medicineName: 'Dextromethorphan 15mg',
              activeIngredient: 'Dextromethorphan HBr',
              strength: '15mg',
              dosageForm: 'Viên nén',
              dispensingUnit: 'Viên',
              prescribedQuantity: 15,
              singleDoseQuantity: 1,
              singleDoseUnit: 'Viên',
              doseScheduleText: 'Sáng 1 viên, Chiều 1 viên, Tối 1 viên',
              frequency: '3 lần/ngày',
              durationDays: 5,
              administrationRoute: 'Uống',
              instructions: 'Uống sau ăn kèm nhiều nước ấm.',
            },
          ],
        },
      ],
    },
  ],
};

export function buildMockMedicalHistoryResponse(
  identifier: string,
  mode: MedicalHistoryLookupMode,
  params?: GetPatientMedicalHistoryQueryParams,
): PatientMedicalHistoryResponseDto {
  const trimmedIdentifier = identifier.trim();
  const base = PATIENT_MEDICAL_HISTORY_MOCK;

  let filteredVisits = [...base.visits];

  if (params?.includeCurrentVisit === false) {
    filteredVisits = filteredVisits.filter((visit) => !visit.isCurrentVisit);
  }

  const normalizedStatus = params?.status?.trim().toUpperCase();
  if (normalizedStatus && normalizedStatus !== 'ALL') {
    filteredVisits = filteredVisits.filter((visit) => {
      if (normalizedStatus === 'FINALIZED' || normalizedStatus === 'DRAFT') {
        return visit.medicalRecord?.status.toUpperCase() === normalizedStatus;
      }
      if (normalizedStatus === 'IN_PROGRESS' || normalizedStatus === 'IN_CONSULTATION') {
        return (
          visit.status.toUpperCase() === 'IN_PROGRESS' ||
          visit.status.toUpperCase() === 'IN_CONSULTATION'
        );
      }
      return visit.status.toUpperCase() === normalizedStatus;
    });
  }

  const normalizedSpecialty = params?.specialtyCode?.trim().toUpperCase();
  if (normalizedSpecialty && normalizedSpecialty !== 'ALL') {
    filteredVisits = filteredVisits.filter(
      (visit) => visit.specialtyCode?.toUpperCase() === normalizedSpecialty,
    );
  }

  if (typeof params?.departmentId === 'number' && params.departmentId > 0) {
    filteredVisits = filteredVisits.filter((visit) => visit.departmentId === params.departmentId);
  }

  if (params?.fromDate?.trim()) {
    const from = params.fromDate.trim();
    filteredVisits = filteredVisits.filter((visit) => visit.visitDate >= from);
  }

  if (params?.toDate?.trim()) {
    const to = params.toDate.trim();
    filteredVisits = filteredVisits.filter((visit) => visit.visitDate <= to);
  }

  if (params?.keyword?.trim()) {
    const kw = params.keyword.trim().toLowerCase();
    filteredVisits = filteredVisits.filter((visit) => {
      const searchable = [
        visit.visitCode,
        visit.reasonForVisit,
        visit.departmentName,
        visit.specialtyName,
        visit.doctorName,
        visit.medicalRecord?.presentingComplaint,
        visit.medicalRecord?.clinicalFindings,
        visit.medicalRecord?.clinicalConclusion,
        ...visit.diagnoses.map(
          (d) => `${d.icd10Code} ${d.icd10NameVi} ${d.clinicalDiagnosis ?? ''}`,
        ),
        ...visit.diagnosticOrders.map(
          (o) => `${o.orderCode} ${o.serviceName} ${o.conclusion ?? ''} ${o.resultText ?? ''}`,
        ),
        ...visit.prescriptions.flatMap((p) => [
          p.prescriptionCode,
          ...p.items.map((i) => `${i.medicineName} ${i.activeIngredient ?? ''}`),
        ]),
      ]
        .filter(Boolean)
        .join(' ')
        .toLowerCase();
      return searchable.includes(kw);
    });
  }

  const limit = params?.limit && params.limit > 0 ? params.limit : 20;
  filteredVisits = filteredVisits.slice(0, limit);

  return {
    ...base,
    patient: {
      ...base.patient,
      patientCode:
        mode === 'patient' && trimmedIdentifier
          ? trimmedIdentifier.toUpperCase()
          : base.patient.patientCode,
    },
    currentVisit: base.currentVisit
      ? {
          ...base.currentVisit,
          visitCode:
            mode === 'visit' && trimmedIdentifier
              ? trimmedIdentifier.toUpperCase()
              : base.currentVisit.visitCode,
        }
      : null,
    visits: filteredVisits,
  };
}
