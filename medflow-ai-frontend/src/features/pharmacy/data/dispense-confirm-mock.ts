import type { DispenseConfirmContext } from '../types/dispense-confirm.types';

export const DISPENSE_CONFIRM_CONTEXT_MOCK: DispenseConfirmContext = {
  rxCode: 'RX-2026-0415-0088',
  ticketNo: '012',
  visitCode: 'VIS-2026-0415-0108',
  issuedAt: '15/04/2026 10:25',
  doctorName: 'BS.CKII Trần Quốc Tuấn (Phòng 204)',
  totalPaidFormatted: '1.293.600 đ',
  paymentMethodLabel: 'VietQR (100% Tự nguyện)',
  regimenBadge:
    'Phác đồ 4 thuốc diệt trừ Helicobacter Pylori (Bismuth Quadruple Therapy - 14 ngày)',
  receiver: {
    fullName: 'NGUYỄN VĂN AN',
    patientCode: 'PAT-2024-0108',
    identityCard: '001092018892',
    phone: '0988 123 456',
    receiverName: 'Nguyễn Văn An (Chính chủ người bệnh)',
    relationship: 'Bản thân',
    isIdentityVerified: true,
  },
  medications: [
    {
      id: 'dmed-1',
      orderIndex: 1,
      drugName: 'Bismuth subsalicylate 300mg',
      qtyLabel: '56 viên',
      batchCode: 'LO-2405',
      expiryDate: '18/09/2027',
      dosageInstruction: 'Sáng 2 viên, Chiều 2 viên (Tổng 4 viên/ngày). Uống trước ăn 30 phút.',
      warningNote:
        'Lưu ý dược lâm sàng: Thuốc có thể làm phân có màu đen sẫm tự nhiên, không đáng lo ngại.',
      tagLabel: 'Thuốc dạ dày',
      tagTone: 'sky',
    },
    {
      id: 'dmed-2',
      orderIndex: 2,
      drugName: 'Nexium 40mg (Esomeprazole)',
      qtyLabel: '28 viên',
      batchCode: 'NX-2402',
      expiryDate: '20/11/2027',
      dosageInstruction:
        'Sáng 1 viên, Tối 1 viên (Tổng 2 viên/ngày). Uống trước ăn sáng/tối 30 phút.',
      warningNote: 'Tuyệt đối không bẻ hoặc nhai nát viên nén bao tan trong ruột.',
      tagLabel: 'Ức chế bơm proton PPI',
      tagTone: 'sky',
    },
    {
      id: 'dmed-3',
      orderIndex: 3,
      drugName: 'Tetracycline Hydrochloride 500mg',
      qtyLabel: '56 viên',
      batchCode: 'TC-2408',
      expiryDate: '30/11/2027',
      dosageInstruction: 'Sáng 1 viên, Trưa 1 viên, Chiều 1 viên, Tối 1 viên (cách 6 giờ).',
      warningNote: 'Cảnh báo: Uống với nhiều nước lọc, không nằm ngay sau khi uống trong 30 phút.',
      tagLabel: 'Kháng sinh • Thay thế',
      tagTone: 'amber',
    },
    {
      id: 'dmed-4',
      orderIndex: 4,
      drugName: 'Klion 500mg (Metronidazole)',
      qtyLabel: '28 viên',
      batchCode: 'KL-2410',
      expiryDate: '24/10/2028',
      dosageInstruction: 'Sáng 1 viên, Chiều 1 viên (Tổng 2 viên/ngày) sau khi ăn no.',
      warningNote:
        'Cảnh báo tương tác: Tuyệt đối KHÔNG uống rượu bia trong suốt quá trình điều trị và sau ngưng 48h.',
      tagLabel: 'Kháng sinh • Kiêng rượu bia',
      tagTone: 'rose',
    },
  ],
  checks: [
    {
      id: 'chk-1',
      label: '1. Đã đối chiếu đúng nhân thân người nhận, số lô, hàm lượng và số lượng thực xuất.',
      checked: true,
    },
    {
      id: 'chk-2',
      label: '2. Người nhận thuốc đã nghe rõ, hiểu các cảnh báo an toàn & hướng dẫn sử dụng.',
      checked: false,
    },
    {
      id: 'chk-3',
      label: '3. Đã kiểm tra niêm phong túi thuốc & gắn tem QR định danh đúng chuẩn GPP.',
      checked: false,
    },
  ],
};
