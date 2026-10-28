import { MOCK_DOCTORS } from './doctor-search-mock';
import type { DoctorDetailProfile } from '../types/doctor-detail.types';

export const DOCTOR_TRAN_QUOC_TUAN_DETAIL: DoctorDetailProfile = {
  id: 'doc-01',
  name: 'BS.CKII Trần Quốc Tuấn',
  title: 'BSCKII',
  roleTitle: 'Bác sĩ Trưởng kíp khám - Khoa Tiêu Hóa - Gan Mật',
  specialty: 'Khoa Tiêu Hóa - Gan Mật',
  room: 'Phòng 204 - Tầng 2, Dãy B',
  statusText: 'Đang tiếp nhận lịch khám ngoại trú',
  price: 250000,
  avatarUrl: '/assets/admin-doctors/list-01.png',
  education: [
    {
      id: 'edu-01',
      title: 'Bác sĩ Đa khoa',
      institution: 'Đại học Y Hà Nội (Hanoi Medical University)',
      badge: 'Tốt nghiệp loại Giỏi',
      type: 'degree',
    },
    {
      id: 'edu-02',
      title: 'Bác sĩ Chuyên khoa II - Chuyên ngành Nội Tiêu Hóa',
      institution: 'Đại học Y Hà Nội (Đào tạo Chuyên sâu Bác sĩ Chuyên khoa Cấp II)',
      type: 'specialist',
    },
    {
      id: 'edu-03',
      title: 'Chứng chỉ Nội soi Tiêu hóa Can thiệp & Tầm soát sớm Ung thư',
      institution: 'Dịch vụ khám ngoại trú • dữ liệu minh họa',
      type: 'certificate',
    },
  ],
  biography:
    'Hơn 15 năm kinh nghiệm công tác lâm sàng trong điều trị các bệnh lý đường tiêu hóa: Viêm loét dạ dày - tá tràng nhiễm Helicobacter Pylori (HP), Trào ngược dạ dày thực quản (GERD), Bệnh lý gan mật & đại tràng mạn. Thông tin dịch vụ theo danh mục cơ sở; tình trạng đặt lịch phụ thuộc khung giờ còn chỗ. phóng đại cao.',
  hashtags: ['#NoiSoiTieuHoa', '#Gerd', '#DaDayTaTrang', '#GanMat', '#TamSoatUngThu', '#NộiSoiNBI'],
  weeklySchedule: [
    {
      dayOfWeek: 'Thứ Hai',
      hasAvailableSlot: true,
      morningTime: '08:00 - 11:30',
      afternoonTime: '14:00 - 16:30',
      statusTag: 'Đang nhận bệnh',
    },
    {
      dayOfWeek: 'Thứ Tư',
      hasAvailableSlot: true,
      note: 'Buổi sáng: Lịch mổ nội soi',
      afternoonTime: '14:00 - 16:30',
      statusTag: 'Đang nhận bệnh',
    },
    {
      dayOfWeek: 'Thứ Năm',
      hasAvailableSlot: true,
      morningTime: '08:00 - 11:30',
      isActive: true,
    },
  ],
};

export const MOCK_DOCTOR_DETAILS: Record<string, DoctorDetailProfile> = {
  'doc-01': DOCTOR_TRAN_QUOC_TUAN_DETAIL,
};

export function getDoctorDetailById(id: string): DoctorDetailProfile | undefined {
  if (MOCK_DOCTOR_DETAILS[id]) {
    return MOCK_DOCTOR_DETAILS[id];
  }

  // Generate sensible profile fallback for any doctor in catalog
  const found = MOCK_DOCTORS.find((d) => d.id === id);
  if (!found) return undefined;

  return {
    id: found.id,
    name: found.name,
    title: found.title,
    roleTitle: `${found.roleTag} - ${found.specialty}`,
    specialty: found.specialty,
    room: found.room,
    statusText: 'Đang tiếp nhận lịch khám ngoại trú',
    price: found.price,
    avatarUrl: found.avatarUrl,
    education: [
      {
        id: `edu-${found.id}-1`,
        title: 'Bác sĩ Đa khoa',
        institution: 'Đại học Y Hà Nội (Hanoi Medical University)',
        badge: 'Tốt nghiệp loại Giỏi',
        type: 'degree',
      },
      {
        id: `edu-${found.id}-2`,
        title: `${found.title} - Chuyên ngành ${found.specialty}`,
        institution: 'Đại học Y Hà Nội / Đào tạo Bác sĩ chuyên khoa',
        type: 'specialist',
      },
      {
        id: `edu-${found.id}-3`,
        title: `Chứng chỉ hành nghề & Điều trị chuyên sâu ${found.specialty}`,
        institution: 'Bộ Y Tế cấp phép hành nghề',
        type: 'certificate',
      },
    ],
    biography: `Hơn ${found.yearsOfExperience} năm kinh nghiệm chuyên môn công tác lâm sàng trong lĩnh vực ${found.specialty}. Tận tâm, chu đáo và thường xuyên tham gia các hội nghị chuyên môn cập nhật phác đồ điều trị tiên tiến nhất.`,
    hashtags: [
      `#${found.specialty.replace(/\s+/g, '')}`,
      '#KhamNgoaiTru',
      '#BacSiChuyenKhoa',
      '#MedFlowAI',
    ],
    weeklySchedule: [
      {
        dayOfWeek: 'Thứ Hai',
        hasAvailableSlot: true,
        morningTime: '08:00 - 11:30',
        afternoonTime: '14:00 - 16:30',
        statusTag: 'Đang nhận bệnh',
      },
      {
        dayOfWeek: 'Thứ Tư',
        hasAvailableSlot: true,
        afternoonTime: '14:00 - 16:30',
        statusTag: 'Đang nhận bệnh',
      },
      {
        dayOfWeek: 'Thứ Năm',
        hasAvailableSlot: true,
        morningTime: '08:00 - 11:30',
        isActive: true,
      },
    ],
  };
}
