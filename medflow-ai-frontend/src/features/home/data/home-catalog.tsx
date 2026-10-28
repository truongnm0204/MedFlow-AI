import type { Doctor, JourneyStep, Specialty } from '../types/home.types';

const svgProps = {
  'aria-hidden': true,
  className: 'w-6 h-6 text-sky-600',
  fill: 'none',
  stroke: 'currentColor',
  viewBox: '0 0 24 24',
} as const;

export const MOCK_SPECIALTIES: Specialty[] = [
  {
    id: 'gastro',
    name: 'Nội Tiêu Hóa & Nội Soi',
    desc: 'Nội soi dạ dày - đại tràng không đau, tầm soát ung thư sớm',
    doctors: 12,
    price: '350.000đ',
    tag: 'Còn slot hôm nay',
    icon: (
      <svg {...svgProps}>
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          strokeWidth="2"
          d="M19.428 15.428a2 2 0 00-1.022-.547l-2.387-.477a6 6 0 00-3.86.517l-.318.158a6 6 0 01-3.86.517L6.05 15.21a2 2 0 00-1.806.547M8 4h8l-1 1v5.172a2 2 0 00.586 1.414l5 5c1.26 1.26.367 3.414-1.415 3.414H4.828c-1.782 0-2.674-2.154-1.414-3.414l5-5A2 2 0 009 10.172V5L8 4z"
        />
      </svg>
    ),
  },
  {
    id: 'cardio',
    name: 'Tim Mạch Can Thiệp',
    desc: 'Siêu âm tim Doppler màu, Holter điện tim 24h, kiểm soát huyết áp',
    doctors: 8,
    price: '400.000đ',
    tag: 'Bác sĩ CKII trực',
    icon: (
      <svg {...svgProps}>
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          strokeWidth="2"
          d="M4.318 6.318a4.5 4.5 0 000 6.364L12 20.364l7.682-7.682a4.5 4.5 0 00-6.364-6.364L12 7.636l-1.318-1.318a4.5 4.5 0 00-6.364 0z"
        />
      </svg>
    ),
  },
  {
    id: 'ent',
    name: 'Tai Mũi Họng Công Nghệ Cao',
    desc: 'Nội soi ống mềm HD không đau, tầm soát vòm họng & thanh quản',
    doctors: 10,
    price: '300.000đ',
    tag: 'Còn slot hôm nay',
    icon: (
      <svg {...svgProps}>
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          strokeWidth="2"
          d="M19 11a7 7 0 01-7 7m0 0a7 7 0 01-7-7m7 7v4m0 0H8m4 0h4m-4-8a3 3 0 100-6 3 3 0 000 6z"
        />
      </svg>
    ),
  },
  {
    id: 'pediatrics',
    name: 'Nhi Khoa Kỹ Thuật Cao',
    desc: 'Khám hô hấp, dinh dưỡng, tiêm chủng dịch vụ không chờ đợi',
    doctors: 15,
    price: '350.000đ',
    tag: 'Không gian thân thiện',
    icon: (
      <svg {...svgProps}>
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          strokeWidth="2"
          d="M14.828 14.828a4 4 0 01-5.656 0M9 10h.01M15 10h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z"
        />
      </svg>
    ),
  },
  {
    id: 'ortho',
    name: 'Cơ Xương Khớp & Phục Hồi',
    desc: 'Siêu âm khớp, tiêm nội khớp dưới hướng dẫn siêu âm',
    doctors: 9,
    price: '350.000đ',
    tag: 'Còn slot hôm nay',
    icon: (
      <svg {...svgProps}>
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          strokeWidth="2"
          d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2"
        />
      </svg>
    ),
  },
  {
    id: 'eye',
    name: 'Mắt & Khúc Xạ Kỹ Thuật Số',
    desc: 'Đo khúc xạ tự động, chụp cắt lớp võng mạc OCT, điều trị khô mắt',
    doctors: 7,
    price: '300.000đ',
    tag: 'Máy đo kỹ thuật số',
    icon: (
      <svg {...svgProps}>
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          strokeWidth="2"
          d="M15 12a3 3 0 11-6 0 3 3 0 016 0z"
        />
        <path
          strokeLinecap="round"
          strokeLinejoin="round"
          strokeWidth="2"
          d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z"
        />
      </svg>
    ),
  },
];

export const MOCK_DOCTORS: Doctor[] = [
  {
    id: 'doc1',
    name: 'TS. BS. Nguyễn Văn Hùng',
    role: 'Trưởng Khoa Nội Tiêu Hóa',
    degree: 'Tiến sĩ Y Khoa • 22 năm kinh nghiệm',
    hospital: 'Đại học Y Hà Nội',
    earliestSlot: '08:45 - 09:00 Hôm nay',
    fee: '450.000đ',
    rating: '4.9/5 (1.240 lượt khám)',
    avatar: 'BS.H',
  },
  {
    id: 'doc2',
    name: 'BS. CKII Trần Mai Anh',
    role: 'Chuyên gia Tim Mạch Can Thiệp',
    degree: 'Bác sĩ CKII • 18 năm kinh nghiệm',
    hospital: 'Viện Tim Mạch Quốc Gia',
    earliestSlot: '09:15 - 09:30 Hôm nay',
    fee: '450.000đ',
    rating: '5.0/5 (980 lượt khám)',
    avatar: 'BS.A',
  },
  {
    id: 'doc3',
    name: 'ThS. BS. Lê Thu Hà',
    role: 'Phó Khoa Tai Mũi Họng',
    degree: 'Thạc sĩ Y Khoa • 15 năm kinh nghiệm',
    hospital: 'BV Tai Mũi Họng Trung Ương',
    earliestSlot: '10:00 - 10:15 Hôm nay',
    fee: '350.000đ',
    rating: '4.9/5 (860 lượt khám)',
    avatar: 'BS.H',
  },
  {
    id: 'doc4',
    name: 'BS. CKI Phạm Quang Minh',
    role: 'Chuyên Khoa Nhi Kỹ Thuật Cao',
    degree: 'Bác sĩ CKI • 12 năm kinh nghiệm',
    hospital: 'BV Nhi Trung Ương',
    earliestSlot: '08:30 - 08:45 Hôm nay',
    fee: '350.000đ',
    rating: '4.9/5 (1.150 lượt khám)',
    avatar: 'BS.M',
  },
];

export const JOURNEY_STEPS: JourneyStep[] = [
  {
    step: '01',
    title: 'Đặt Hẹn Online',
    desc: 'Chọn bác sĩ, chuyên khoa và khung giờ trống mong muốn trong 1 phút.',
  },
  {
    step: '02',
    title: 'Quét QR Kiosk 5s',
    desc: 'Đến viện chỉ cần quét mã QR tại Kiosk để nhận số thứ tự vào thẳng phòng khám.',
  },
  {
    step: '03',
    title: 'Khám & CLS Số',
    desc: 'Bác sĩ thăm khám kỹ lưỡng, chỉ định xét nghiệm & chẩn đoán hình ảnh đồng bộ.',
  },
  {
    step: '04',
    title: 'VietQR & Đơn Số',
    desc: 'Thanh toán tức thì qua mã QR động và xem kết quả hồ sơ bệnh án EMR trên máy bay.',
  },
];
