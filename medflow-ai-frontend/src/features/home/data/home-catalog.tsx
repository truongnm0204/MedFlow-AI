import { Activity, Eye, FlaskConical, HeartPulse, Mic, Smile } from 'lucide-react';

import type { Doctor, JourneyStep, Specialty } from '../types/home.types';

const iconClassName = 'h-6 w-6 text-primary';

export const MOCK_SPECIALTIES: Specialty[] = [
  {
    id: 'gastro',
    name: 'Nội Tiêu Hóa & Nội Soi',
    desc: 'Nội soi dạ dày - đại tràng không đau, tầm soát ung thư sớm',
    doctors: 0,
    price: 'Theo bảng giá cơ sở',
    tag: 'Khám chuyên khoa',
    icon: <FlaskConical aria-hidden="true" className={iconClassName} />,
  },
  {
    id: 'cardio',
    name: 'Tim Mạch Can Thiệp',
    desc: 'Siêu âm tim Doppler màu, Holter điện tim 24h, kiểm soát huyết áp',
    doctors: 0,
    price: 'Theo bảng giá cơ sở',
    tag: 'Bác sĩ CKII trực',
    icon: <HeartPulse aria-hidden="true" className={iconClassName} />,
  },
  {
    id: 'ent',
    name: 'Tai Mũi Họng Công Nghệ Cao',
    desc: 'Nội soi ống mềm HD không đau, tầm soát vòm họng & thanh quản',
    doctors: 0,
    price: 'Theo bảng giá cơ sở',
    tag: 'Khám chuyên khoa',
    icon: <Mic aria-hidden="true" className={iconClassName} />,
  },
  {
    id: 'pediatrics',
    name: 'Nhi Khoa Kỹ Thuật Cao',
    desc: 'Khám hô hấp, dinh dưỡng, tiêm chủng dịch vụ không chờ đợi',
    doctors: 0,
    price: 'Theo bảng giá cơ sở',
    tag: 'Không gian thân thiện',
    icon: <Smile aria-hidden="true" className={iconClassName} />,
  },
  {
    id: 'ortho',
    name: 'Cơ Xương Khớp & Phục Hồi',
    desc: 'Siêu âm khớp, tiêm nội khớp dưới hướng dẫn siêu âm',
    doctors: 0,
    price: 'Theo bảng giá cơ sở',
    tag: 'Khám chuyên khoa',
    icon: <Activity aria-hidden="true" className={iconClassName} />,
  },
  {
    id: 'eye',
    name: 'Mắt & Khúc Xạ Kỹ Thuật Số',
    desc: 'Đo khúc xạ tự động, chụp cắt lớp võng mạc OCT, điều trị khô mắt',
    doctors: 0,
    price: 'Theo bảng giá cơ sở',
    tag: 'Máy đo kỹ thuật số',
    icon: <Eye aria-hidden="true" className={iconClassName} />,
  },
];

export const MOCK_DOCTORS: Doctor[] = [
  {
    id: 'doc1',
    name: 'TS. BS. Nguyễn Văn Hùng',
    role: 'Trưởng Khoa Nội Tiêu Hóa',
    degree: 'Tiến sĩ Y Khoa',
    hospital: 'Khối Nội Tiêu Hóa',
    earliestSlot: '',
    fee: '',
    rating: '',
    avatar: 'VH',
  },
  {
    id: 'doc2',
    name: 'BS. CKII Trần Mai Anh',
    role: 'Chuyên gia Tim Mạch Can Thiệp',
    degree: 'Bác sĩ Chuyên khoa II',
    hospital: 'Khối Tim Mạch',
    earliestSlot: '',
    fee: '',
    rating: '',
    avatar: 'MA',
  },
  {
    id: 'doc3',
    name: 'ThS. BS. Lê Thu Hà',
    role: 'Phó Khoa Tai Mũi Họng',
    degree: 'Thạc sĩ Y Khoa',
    hospital: 'Khối Tai Mũi Họng',
    earliestSlot: '',
    fee: '',
    rating: '',
    avatar: 'TH',
  },
  {
    id: 'doc4',
    name: 'BS. CKI Phạm Quang Minh',
    role: 'Bác sĩ Nhi Kỹ Thuật Cao',
    degree: 'Bác sĩ Chuyên khoa I',
    hospital: 'Khối Nhi Ngoại Trú',
    earliestSlot: '',
    fee: '',
    rating: '',
    avatar: 'QM',
  },
];

export const JOURNEY_STEPS: JourneyStep[] = [
  {
    step: '01',
    title: 'Đặt Hẹn Online',
    desc: 'Chọn bác sĩ, chuyên khoa và khung giờ trống mong muốn trực tuyến.',
  },
  {
    step: '02',
    title: 'Quét QR Kiosk',
    desc: 'Đến viện quét mã QR tại Kiosk để nhận số thứ tự vào thẳng phòng khám.',
  },
  {
    step: '03',
    title: 'Khám & CLS Số',
    desc: 'Bác sĩ thăm khám kỹ lưỡng, chỉ định xét nghiệm & chẩn đoán hình ảnh đồng bộ.',
  },
  {
    step: '04',
    title: 'VietQR & Đơn Số',
    desc: 'Thanh toán qua mã QR động và tra cứu kết quả hồ sơ bệnh án EMR trực tuyến.',
  },
];
