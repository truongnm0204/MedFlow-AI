import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { DepartmentDeactivateModal } from '../components/department-deactivate-modal';
import type { DepartmentItem } from '../types/department.types';

const eligibleDepartment: DepartmentItem = {
  id: 'dep-003',
  code: 'DEP-003',
  name: 'Khoa Nội tiết',
  facility: 'MedFlow Cơ sở 1',
  status: 'ACTIVE',
  activeRoomsCount: 0,
  activeDoctorsCount: 0,
  activeServicesCount: 0,
  pendingAppointmentsCount: 0,
  hasActiveDependencies: false,
};

const blockedDepartment: DepartmentItem = {
  id: 'dep-001',
  code: 'DEP-001',
  name: 'Khoa Nội Tiêu hóa',
  facility: 'MedFlow Cơ sở 1',
  status: 'ACTIVE',
  activeRoomsCount: 2,
  activeDoctorsCount: 3,
  activeServicesCount: 2,
  pendingAppointmentsCount: 4,
  hasActiveDependencies: true,
};

describe('DepartmentDeactivateModal (SCR-ADM-DEP-03)', () => {
  it('does not render when open is false', () => {
    const { container } = render(
      <DepartmentDeactivateModal
        department={eligibleDepartment}
        open={false}
        onClose={vi.fn()}
        onConfirm={vi.fn()}
      />,
    );
    expect(container.firstChild).toBeNull();
  });

  it('renders modal with title, department information, and description', () => {
    render(
      <DepartmentDeactivateModal
        department={eligibleDepartment}
        open={true}
        onClose={vi.fn()}
        onConfirm={vi.fn()}
      />,
    );

    expect(
      screen.getByRole('heading', { name: 'Ngừng hoạt động khoa phòng?' }),
    ).toBeInTheDocument();
    expect(screen.getByText('Khoa Nội tiết')).toBeInTheDocument();
    expect(screen.getByText('DEP-003')).toBeInTheDocument();
    expect(screen.getByText(/Khoa phòng sẽ chuyển sang trạng thái/i)).toBeInTheDocument();
    expect(screen.getByText('Điều kiện kiểm tra hợp lệ')).toBeInTheDocument();
    expect(
      screen.getByText(/Xóa cứng không áp dụng: hồ sơ và mọi liên kết lịch sử được bảo toàn/i),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Hủy bỏ' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Ngừng hoạt động' })).toBeInTheDocument();
  });

  it('calls onClose when "Hủy bỏ" is clicked', () => {
    const onClose = vi.fn();
    render(
      <DepartmentDeactivateModal
        department={eligibleDepartment}
        open={true}
        onClose={onClose}
        onConfirm={vi.fn()}
      />,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Hủy bỏ' }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('submits deactivation payload when eligible department is confirmed', async () => {
    const onConfirm = vi.fn();
    render(
      <DepartmentDeactivateModal
        department={eligibleDepartment}
        open={true}
        onClose={vi.fn()}
        onConfirm={onConfirm}
      />,
    );

    fireEvent.change(screen.getByLabelText(/Lý do ngừng hoạt động/i), {
      target: { value: 'MAINTENANCE' },
    });
    fireEvent.change(screen.getByLabelText(/Ghi chú bổ sung/i), {
      target: { value: 'Sửa chữa bảo trì hệ thống điều hòa khu C' },
    });

    const confirmButton = screen.getByRole('button', { name: 'Ngừng hoạt động' });
    expect(confirmButton).not.toBeDisabled();
    fireEvent.click(confirmButton);

    expect(onConfirm).toHaveBeenCalledWith('dep-003', {
      departmentId: 'dep-003',
      reason: 'MAINTENANCE',
      note: 'Sửa chữa bảo trì hệ thống điều hòa khu C',
    });
  });

  it('disables submit button and shows blocking alert when department has active dependencies', () => {
    const onConfirm = vi.fn();
    render(
      <DepartmentDeactivateModal
        department={blockedDepartment}
        open={true}
        onClose={vi.fn()}
        onConfirm={onConfirm}
      />,
    );

    expect(
      screen.getByText('Không thể ngừng hoạt động (Còn ràng buộc đang vận hành)'),
    ).toBeInTheDocument();
    expect(
      screen.getByText(/Khoa phòng đang có 2 buồng khám, 3 bác sĩ, 4 lượt khám đang hoạt động/i),
    ).toBeInTheDocument();

    const confirmButton = screen.getByRole('button', { name: 'Ngừng hoạt động' });
    expect(confirmButton).toBeDisabled();

    fireEvent.click(confirmButton);
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it('closes on Escape key press', () => {
    const onClose = vi.fn();
    render(
      <DepartmentDeactivateModal
        department={eligibleDepartment}
        open={true}
        onClose={onClose}
        onConfirm={vi.fn()}
      />,
    );

    fireEvent.keyDown(document, { key: 'Escape' });
    expect(onClose).toHaveBeenCalled();
  });
});
