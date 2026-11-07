import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { DoctorDeactivateModal } from '../components/doctor-deactivate-modal';
import type { Doctor } from '../types/doctor.types';

const mockDoctor: Doctor = {
  id: 'DOC-0142',
  staffId: 'NV-1142',
  name: 'BS. CKI Lê Hoàng Minh',
  email: 'minh.lh@medflow.vn',
  degree: 'Bác sĩ Chuyên khoa I',
  specialty: 'Nội Tiêu hóa - Gan Mật',
  department: 'Khoa Nội Tiêu hóa',
  license: '004812/BYT-CCHN',
  room: 'PK-101 (Phòng Tiêu hóa 1)',
  phone: '0912.456.789',
  experience: 12,
  scope: 'Khám bệnh, chữa bệnh chuyên khoa Nội Tiêu hóa - Gan Mật',
  biography: 'Hơn 12 năm kinh nghiệm trong chẩn đoán và điều trị.',
  active: true,
  history: [],
};

describe('DoctorDeactivateModal (SCR-ADM-DOC-05)', () => {
  it('does not render when open is false', () => {
    const { container } = render(
      <DoctorDeactivateModal
        doctor={mockDoctor}
        open={false}
        onClose={vi.fn()}
        onConfirm={vi.fn()}
      />,
    );
    expect(container.firstChild).toBeNull();
  });

  it('renders modal with Figma title, doctor name, and description', () => {
    render(
      <DoctorDeactivateModal
        doctor={mockDoctor}
        open={true}
        onClose={vi.fn()}
        onConfirm={vi.fn()}
      />,
    );

    expect(screen.getByRole('heading', { name: 'Ngừng hoạt động bác sĩ?' })).toBeInTheDocument();
    expect(screen.getByText(/BS\. CKI Lê Hoàng Minh/)).toBeInTheDocument();
    expect(screen.getByText('DOC-0142')).toBeInTheDocument();
    expect(screen.getByText(/Bác sĩ sẽ không nhận lịch hoặc phân công mới/i)).toBeInTheDocument();
    expect(screen.getByText('Trước khi xác nhận')).toBeInTheDocument();
    expect(
      screen.getByText(
        'Không thể ngừng hoạt động khi còn lượt khám đang xử lý hoặc lịch tương lai chưa được giải quyết.',
      ),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Xóa cứng không áp dụng: hồ sơ và mọi liên kết lịch sử được bảo toàn/i),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Giữ hồ sơ' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ngừng hoạt động' })).toBeInTheDocument();
  });

  it('calls onClose when "Giữ hồ sơ" button is clicked', () => {
    const onClose = vi.fn();
    render(
      <DoctorDeactivateModal
        doctor={mockDoctor}
        open={true}
        onClose={onClose}
        onConfirm={vi.fn()}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Giữ hồ sơ' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('calls onConfirm with doctor ID when "Ngừng hoạt động" button is clicked', async () => {
    const onConfirm = vi.fn();
    render(
      <DoctorDeactivateModal
        doctor={mockDoctor}
        open={true}
        onClose={vi.fn()}
        onConfirm={onConfirm}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Ngừng hoạt động' }));
    expect(onConfirm).toHaveBeenCalledWith('DOC-0142');
  });

  it('disables confirmation button and shows warning when doctor has pending appointments', () => {
    const blockedDoctor: Doctor = {
      ...mockDoctor,
      hasPendingAppointments: true,
      pendingAppointmentsCount: 3,
    };
    const onConfirm = vi.fn();

    render(
      <DoctorDeactivateModal
        doctor={blockedDoctor}
        open={true}
        onClose={vi.fn()}
        onConfirm={onConfirm}
      />,
    );

    const confirmButton = screen.getByRole('button', { name: 'Ngừng hoạt động' });
    expect(confirmButton).toBeDisabled();
    expect(
      screen.getByText(/Không thể ngừng hoạt động khi còn 3 lượt khám đang xử lý/i),
    ).toBeInTheDocument();

    fireEvent.click(confirmButton);
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it('closes on Escape key press', () => {
    const onClose = vi.fn();
    render(
      <DoctorDeactivateModal
        doctor={mockDoctor}
        open={true}
        onClose={onClose}
        onConfirm={vi.fn()}
      />,
    );

    fireEvent.keyDown(document, { key: 'Escape' });
    expect(onClose).toHaveBeenCalled();
  });
});
