import type { ReviewDiagnosticData } from '../types/review-diagnostic.types';

export const REVIEW_DIAGNOSTIC_STEPS = [
  'Tiếp nhận & Sinh hiệu',
  'Khám lâm sàng',
  'Chỉ định CLS',
  'Kết quả CLS trả về (SCR-DIA-02)',
  'Chẩn đoán & Kết luận',
] as const;

export const REVIEW_DIAGNOSTIC_ACTIVE_STEP = 4;

export const REVIEW_DIAGNOSTIC_MOCK: ReviewDiagnosticData = {
  patient: {
    fullName: 'NGUYỄN VĂN AN',
    patientCode: 'PAT-2024-0108',
    visitCode: 'VIS-2026-0415-0108',
    allergies: ['Penicillin', 'NSAIDs'],
  },
  results: [
    {
      id: 'dia-endo-01',
      orderIndex: 1,
      serviceCode: 'CLS-ENDO-01',
      serviceName: 'Nội soi thực quản - dạ dày - tá tràng ống mềm có tiền mê (kèm Clo-Test HP)',
      performedBy: 'BS.CKI Hoàng Minh Hải',
      signedAtLabel: '15/04/2026 09:42',
      conclusionSummary:
        'Viêm teo niêm mạc hang vị mức độ vừa, kèm loét hành tá tràng Forrest III. Clo-Test dương tính.',
      alertTag: { label: 'HP DƯƠNG TÍNH (+)', tone: 'rose' },
      labIndicators: [],
      imageFrames: [
        { id: 'endo-f1', caption: 'Thực quản 1/3 dưới - niêm mạc hồng nhạt' },
        { id: 'endo-f2', caption: 'Thân vị - nếp niêm mạc phù nề' },
        { id: 'endo-f3', caption: 'Hang vị - viêm teo niêm mạc' },
        { id: 'endo-f4', caption: 'Hành tá tràng - ổ loét 5mm' },
      ],
      anatomicalDescription: [
        'Thực quản: niêm mạc hồng, không giãn tĩnh mạch, tâm vị đóng kín.',
        'Dạ dày: niêm mạc thân vị phù nề nhẹ; hang vị sung huyết, teo niêm mạc từng đám.',
        'Hành tá tràng: ổ loét đường kính khoảng 5mm, đáy phủ giả mạc trắng, không chảy máu.',
        'Clo-Test HP: dương tính sau 15 phút.',
      ],
      recommendation:
        'Đề nghị điều trị tiệt trừ H. pylori phác đồ 14 ngày (lưu ý tiền sử dị ứng Penicillin) và tái nội soi kiểm tra sau 8 tuần.',
      pdfFileName: 'KQ-CLS-ENDO-01.pdf',
    },
    {
      id: 'dia-us-02',
      orderIndex: 2,
      serviceCode: 'CLS-US-02',
      serviceName: 'Siêu âm ổ bụng tổng quát màu Doppler (4D)',
      performedBy: 'ThS.BS Nguyễn Thu Trang',
      signedAtLabel: '15/04/2026 08:55',
      conclusionSummary:
        'Gan nhiễm mỡ độ I. Túi mật, tụy, lách, hai thận chưa ghi nhận bất thường.',
      labIndicators: [],
      imageFrames: [
        { id: 'us-f1', caption: 'Gan phải - mặt cắt dưới sườn' },
        { id: 'us-f2', caption: 'Túi mật - thành mỏng, không sỏi' },
        { id: 'us-f3', caption: 'Thận phải - mặt cắt dọc' },
        { id: 'us-f4', caption: 'Doppler tĩnh mạch cửa' },
      ],
      anatomicalDescription: [
        'Gan: kích thước bình thường, echo dày nhẹ lan toả, bờ đều.',
        'Túi mật: thành mỏng, không thấy sỏi. Đường mật trong và ngoài gan không giãn.',
        'Tụy, lách, hai thận: kích thước và cấu trúc echo trong giới hạn bình thường.',
        'Doppler tĩnh mạch cửa: dòng chảy hướng gan, vận tốc bình thường.',
      ],
      recommendation: 'Điều chỉnh chế độ ăn, theo dõi men gan và siêu âm kiểm tra lại sau 6 tháng.',
      pdfFileName: 'KQ-CLS-US-02.pdf',
    },
    {
      id: 'dia-lab-01',
      orderIndex: 3,
      serviceCode: 'CLS-LAB-01',
      serviceName: 'Tổng phân tích tế bào máu ngoại vi bằng laser (CBC 24 thông số)',
      performedBy: 'ThS.BS Lê Quốc Bảo',
      signedAtLabel: '15/04/2026 08:20',
      conclusionSummary:
        'Bạch cầu tăng nhẹ, các dòng hồng cầu và tiểu cầu trong giới hạn bình thường.',
      labIndicators: [
        {
          code: 'WBC',
          name: 'Bạch cầu',
          value: '11.2',
          unit: 'G/L',
          referenceRange: '4.0 - 10.0',
          flag: 'HIGH',
        },
        {
          code: 'RBC',
          name: 'Hồng cầu',
          value: '4.8',
          unit: 'T/L',
          referenceRange: '4.2 - 5.8',
          flag: 'NORMAL',
        },
        {
          code: 'HGB',
          name: 'Huyết sắc tố',
          value: '142',
          unit: 'g/L',
          referenceRange: '130 - 175',
          flag: 'NORMAL',
        },
        {
          code: 'PLT',
          name: 'Tiểu cầu',
          value: '265',
          unit: 'G/L',
          referenceRange: '150 - 400',
          flag: 'NORMAL',
        },
      ],
      imageFrames: [],
      anatomicalDescription: [
        'Mẫu máu tĩnh mạch ống EDTA, phân tích bằng máy laser 24 thông số.',
        'Dòng bạch cầu tăng nhẹ, công thức bạch cầu ưu thế trung tính.',
      ],
      recommendation:
        'Đối chiếu với lâm sàng và kết quả nội soi; theo dõi công thức máu sau điều trị.',
      pdfFileName: 'KQ-CLS-LAB-01.pdf',
    },
  ],
};
