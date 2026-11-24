import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { PatientDetailModal } from '../components/patient-detail-modal';
import type { PatientDetail } from '../types/patient-detail.types';

const patient: PatientDetail = {
  id: 'p-0018',
  patientCode: 'BN-2024-0018',
  fullName: 'Nguyễn Văn An',
  gender: 'MALE',
  age: 34,
  dateOfBirth: '14/05/1992',
  cccd: '0010 8801 5708',
  phone: '0912 345 678',
  bhytNumber: 'DN4010123456789',
  address: '25 Nguyễn Huệ, Phường Bến Nghé, Quận 1, TP. Hồ Chí Minh',
  emergencyContact: { name: 'Nguyễn Thị Hoa', relationship: 'Vợ', phone: '0987 654 321' },
  todayAppointment: {
    timeRange: '09:00 - 09:30',
    doctorName: 'BS. CKII Trần Khánh',
    room: 'P.102',
    status: 'ARRIVED',
  },
  pastVisits: [
    {
      id: 'v-003',
      date: '12/03/2026',
      department: 'Nội tổng quát',
      doctorName: 'BS. CKII Trần Khánh',
      diagnosis: 'Viêm dạ dày cấp, theo dõi sau điều trị',
    },
    {
      id: 'v-002',
      date: '20/11/2025',
      department: 'Tim mạch',
      doctorName: 'BS. Lê Hoàng Nam',
      diagnosis: 'Tăng huyết áp độ 1',
    },
    {
      id: 'v-001',
      date: '05/06/2025',
      department: 'Tai mũi họng',
      doctorName: 'BS. Phạm Thu Hà',
      diagnosis: 'Viêm họng cấp',
    },
  ],
};

describe('PatientDetailModal', () => {
  it('khong render khi patient = null', () => {
    render(<PatientDetailModal patient={null} onClose={() => undefined} />);
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('render thong tin benh nhan va lich su kham', () => {
    render(<PatientDetailModal patient={patient} onClose={() => undefined} />);

    expect(screen.getByText(/Nguyễn Văn An • Nam, 34 tuổi/)).toBeInTheDocument();
    expect(screen.getByText('BN-2024-0018')).toBeInTheDocument();
    expect(screen.getByText(/0010 8801 5708/)).toBeInTheDocument();
    expect(screen.getByText('DN4010123456789')).toBeInTheDocument();
    expect(screen.getByText('ĐÃ CÓ MẶT TẠI SẢNH')).toBeInTheDocument();
    expect(screen.getByText('Viêm họng cấp')).toBeInTheDocument();
    expect(screen.getAllByRole('row')).toHaveLength(patient.pastVisits.length + 1);
  });

  it('goi onClose khi bam Dong', () => {
    const onClose = vi.fn();
    render(<PatientDetailModal patient={patient} onClose={onClose} />);
    fireEvent.click(screen.getByRole('button', { name: 'Đóng hộp thoại' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('goi onActivate khi bam Tiep nhan', () => {
    const onActivate = vi.fn();
    render(
      <PatientDetailModal patient={patient} onClose={() => undefined} onActivate={onActivate} />,
    );
    fireEvent.click(screen.getByRole('button', { name: 'Tiếp nhận / Kích hoạt lượt khám' }));
    expect(onActivate).toHaveBeenCalledWith(patient);
  });
});
