import type { DiagnosticResultDetailView } from '../types/diagnostic-result-view.types';

export const DIAGNOSTIC_RESULT_VIEW_MOCK: DiagnosticResultDetailView = {
  orderId: 'ord-2026-0142',
  orderCode: 'ORD-2026-0415-0142',
  serviceName: 'Nội soi thực quản - dạ dày - tá tràng ống mềm có tiền mê (kèm Clo-Test HP)',
  patientCode: 'PAT-2024-0108',
  patientName: 'NGUYỄN VĂN AN',
  gender: 'Nam',
  ageLabel: '34 tuổi (1992)',
  visitCode: 'VIS-2026-0415-0108',
  allergyWarning: 'CẢNH BÁO DỊ ỨNG: DỊ ỨNG PENICILLIN (Phù mạch, mề đay)',
  orderingDoctor: 'BS.CKI Trần Quốc Tuấn (Phòng 204)',
  performingDoctor: 'BS.CKI Hoàng Minh Hải',
  roomName: 'Buồng Nội soi Tiêu hóa 102',
  equipmentName: 'Olympus GIF-XZ1200 (EVIS X1)',
  priceFormatted: '1.250.000 đ',
  performedAt: '15/04/2026 • 10:05 - 10:18 (13 phút)',
  signedAt: '15/04/2026 10:22',
  deviceBadge: 'CHỤP TỪ MÁY OLYMPUS EVIS X1',
  images: [
    {
      id: 'img-1',
      label: 'Thực quản 1/3 dưới & Đường Z (38cm)',
      caption: 'Niêm mạc thực quản hồng nhẵn, đường Z rõ nét ở 38cm, không viêm loét.',
      location: 'Thực quản',
      isAbnormal: false,
    },
    {
      id: 'img-2',
      label: 'Phình vị và thân vị dạ dày',
      caption: 'Nếp niêm mạc thân vị mềm mại, không xuất huyết, dịch vị trong.',
      location: 'Thân vị',
      isAbnormal: false,
    },
    {
      id: 'img-3',
      label: 'Hang vị & Bờ cong nhỏ (Tổn thương)',
      caption: 'Niêm mạc hang vị sung huyết lan tỏa, vài ổ trợt nông kích thước 2-3mm.',
      location: 'Hang vị',
      isAbnormal: true,
    },
    {
      id: 'img-4',
      label: 'Môn vị tròn đều & Hành tá tràng',
      caption: 'Lỗ môn vị mở tròn đều, co bóp nhịp nhàng; hành tá tràng niêm mạc hồng.',
      location: 'Tá tràng',
      isAbnormal: false,
    },
  ],
  microbiologyTest: {
    name: 'Test nhanh vi khuẩn Helicobacter pylori (Clo-Test / Urease test)',
    resultLabel: 'ÂM TÍNH (NEGATIVE)',
    isPositive: false,
    notes:
      'Đọc kết quả ở phút thứ 15 tại buồng soi: Vùng gel chỉ thị màu vàng chanh đồng nhất, không chuyển màu hồng cánh sen.',
  },
  videoAttachment: {
    fileName: 'REC_20260415_1015_0108.mp4',
    durationLabel: 'Video thủ thuật 4K NBI lưu trữ tại máy chủ PACS • 04:12',
  },
  anatomicalSections: [
    {
      id: 'sec-esophagus',
      organName: '1. Thực quản (Esophagus)',
      statusLabel: 'Bình thường',
      statusTone: 'emerald',
      findings: [
        {
          label: 'Niêm mạc & Đường Z',
          detail:
            'Niêm mạc thực quản trơn láng, có lớp niêm mạc, đường Z nằm ở vị trí 38cm cách cung răng. Không loét trượt, không polyp, không giãn tĩnh mạch thực quản.',
        },
      ],
    },
    {
      id: 'sec-stomach',
      organName: '2. Dạ dày (Stomach)',
      statusLabel: 'Có tổn thương viêm trợt',
      statusTone: 'amber',
      findings: [
        { label: 'Dịch vị dạ dày', detail: 'Dịch trong, lượng vừa, không lẫn máu hoặc dịch mật.' },
        {
          label: 'Phình vị & Thân vị',
          detail: 'Niêm mạc hồng đều, nếp niêm mạc giãn nở tốt khi bơm hơi.',
        },
        {
          label: 'Hang vị & Bờ cong nhỏ',
          detail:
            'Niêm mạc phù nề, sung huyết lan tỏa, rải rác có một vài trợt nông kích thước 2-3mm và vài nốt sung huyết nhỏ. Không có ổ loét sâu, không chảy máu.',
        },
        { label: 'Môn vị (Pylorus)', detail: 'Tròn đều, co bóp nhịp nhàng, đóng mở tốt.' },
      ],
    },
    {
      id: 'sec-duodenum',
      organName: '3. Tá tràng (Duodenum - Bulba & D2)',
      statusLabel: 'Bình thường',
      statusTone: 'emerald',
      findings: [
        {
          label: 'Hành tá tràng & D2',
          detail:
            'Hành tá tràng và đoạn D2 tá tràng niêm mạc nhẵn bóng, không viêm loét, không polyp.',
        },
      ],
    },
  ],
  procedureChecklist: [
    { id: 'chk-biopsy', label: 'Không sinh thiết giải phẫu bệnh', checked: true },
    { id: 'chk-hemostasis', label: 'Không can thiệp cầm máu', checked: false },
    { id: 'chk-urease', label: 'Đã làm Test Urease HP tại buồng soi', checked: true },
  ],
  clinicalConclusion:
    'Viêm sung huyết, trợt nông niêm mạc hang vị dạ dày mức độ nhẹ. Test HP (Urease test): Âm tính.',
  icd10Tags: [
    { code: 'K29.6', name: 'Các viêm dạ dày khác (Other gastritis)' },
    { code: 'K29.5', name: 'Viêm dạ dày mạn tính không đặc hiệu' },
  ],
  followUpAdvice:
    'Bệnh nhân ăn cháo nguội hoặc súp lỏng sau soi 2 giờ. Tránh đồ chua cay, chất kích thích. Quay về Phòng khám 204 gặp BS.CKI Trần Quốc Tuấn để nhận phác đồ điều trị.',
};
