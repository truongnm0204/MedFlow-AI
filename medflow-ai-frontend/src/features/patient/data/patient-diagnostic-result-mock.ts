import type { PatientDiagnosticResultSheet } from '../types/patient-diagnostic-result.types';

export const PATIENT_DIAGNOSTIC_RESULT_MOCK: PatientDiagnosticResultSheet = {
  fileName: 'KQ-CLS-VIS-2026-0415-0108.pdf',
  orderCode: 'ORD-2026-0415-0142',
  visitCode: 'VIS-2026-0415-0108',
  patientName: 'NGUYỄN VĂN AN',
  patientCode: 'PAT-2024-0108',
  gender: 'Nam',
  birthYear: 1992,
  serviceName: 'Nội soi thực quản - dạ dày - tá tràng ống mềm & Tổng phân tích tế bào máu',
  orderingDoctor: 'BS.CKII Trần Quốc Tuấn (Phòng 204)',
  performingDoctor: 'BS.CKI Hoàng Minh Hải',
  performedRoom: 'Buồng Nội soi Tiêu hóa 102 • Tầng 1, Nhà B',
  signedAt: '15/04/2026 10:22',
  isPublishedToPatient: true,
  images: [
    {
      id: 'pimg-1',
      label: 'Hình 1: Thực quản 1/3 dưới & Đường Z',
      description: 'Niêm mạc thực quản hồng nhẵn, đường Z rõ ở 38cm, không giãn tĩnh mạch.',
    },
    {
      id: 'pimg-2',
      label: 'Hình 2: Phình vị & Thân vị dạ dày',
      description: 'Nếp niêm mạc thân vị mềm mại, không xuất huyết.',
    },
    {
      id: 'pimg-3',
      label: 'Hình 3: Hang vị dạ dày (Tổn thương trợt nông)',
      description: 'Niêm mạc hang vị sung huyết lan tỏa, vài ổ trợt nông 2-3mm.',
    },
    {
      id: 'pimg-4',
      label: 'Hình 4: Hành tá tràng & Môn vị',
      description: 'Lỗ môn vị tròn đều, co bóp tốt; hành tá tràng không loét.',
    },
  ],
  endoscopicConclusion:
    'Viêm sung huyết, trợt nông niêm mạc hang vị dạ dày mức độ vừa. Test nhanh Urease HP: Âm tính.',
  endoscopicFindings: [
    'Thực quản: Niêm mạc hồng đều, không viêm loét.',
    'Dạ dày: Hang vị sung huyết trợt nông nhẹ, không chảy máu đang diễn tiến.',
    'Tá tràng: Hành tá tràng và đoạn D2 bình thường.',
  ],
  labSectionTitle: 'KẾT QUẢ XÉT NGHIỆM HUYẾT HỌC TỰ ĐỘNG (CBC 24 THÔNG SỐ)',
  labMetrics: [
    {
      id: 'lab-wbc',
      parameterName: 'Bạch cầu (WBC)',
      value: '6.8',
      unit: 'G/L',
      referenceRange: '4.0 - 10.0',
      status: 'NORMAL',
    },
    {
      id: 'lab-rbc',
      parameterName: 'Hồng cầu (RBC)',
      value: '4.92',
      unit: 'T/L',
      referenceRange: '4.2 - 5.9',
      status: 'NORMAL',
    },
    {
      id: 'lab-hgb',
      parameterName: 'Huyết sắc tố (HGB)',
      value: '148',
      unit: 'g/L',
      referenceRange: '130 - 170',
      status: 'NORMAL',
    },
    {
      id: 'lab-plt',
      parameterName: 'Tiểu cầu (PLT)',
      value: '245',
      unit: 'G/L',
      referenceRange: '150 - 400',
      status: 'NORMAL',
    },
  ],
  physicianAdvice:
    'Người bệnh ăn đồ mềm, dễ tiêu sau khi làm thủ thuật 2 giờ. Tuân thủ đơn thuốc và tái khám theo lịch hẹn của bác sĩ.',
};
