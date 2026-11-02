import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { CreatePatientModal } from '../components/create-patient-modal';

function setup() {
  const onClose = vi.fn();
  const onSubmit = vi
    .fn()
    .mockResolvedValue({ patientCode: 'BN-2024-0001', fullName: 'Nguyễn Văn A' });
  render(<CreatePatientModal open onClose={onClose} onSubmit={onSubmit} />);
  return { onClose, onSubmit };
}

describe('CreatePatientModal', () => {
  it('render các section và field', () => {
    setup();
    expect(screen.getByText('Thông tin định danh')).toBeInTheDocument();
    expect(screen.getByText('Thông tin liên hệ')).toBeInTheDocument();
    expect(screen.getByText('Bảo hiểm y tế')).toBeInTheDocument();
    expect(screen.getByText('Người liên hệ khẩn cấp')).toBeInTheDocument();
    expect(screen.getByLabelText('Họ và tên')).toBeInTheDocument();
    expect(screen.getByLabelText('Dân tộc')).toHaveValue('Kinh');
  });

  it('không render khi đóng', () => {
    render(<CreatePatientModal open={false} onClose={vi.fn()} onSubmit={vi.fn()} />);
    expect(screen.queryByText('Thông tin định danh')).not.toBeInTheDocument();
  });

  it('báo lỗi khi bỏ trống field bắt buộc', async () => {
    const { onSubmit } = setup();
    await userEvent.click(screen.getByRole('button', { name: 'Tạo hồ sơ bệnh nhân' }));
    expect(await screen.findByText('Họ và tên tối thiểu 2 ký tự')).toBeInTheDocument();
    expect(screen.getByText('Vui lòng nhập ngày sinh')).toBeInTheDocument();
    expect(screen.getByText('Vui lòng nhập địa chỉ thường trú')).toBeInTheDocument();
    expect(screen.getByText(/Số điện thoại phải gồm 10 chữ số/)).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('submit thành công hiển thị mã bệnh nhân', async () => {
    const { onSubmit } = setup();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Họ và tên'), 'Nguyễn Văn A');
    await user.type(screen.getByLabelText('Ngày sinh'), '1990-05-20');
    await user.type(screen.getByLabelText('Số điện thoại'), '0912345678');
    await user.type(screen.getByLabelText('Địa chỉ thường trú'), '1 Lê Lợi, Hà Nội');
    await user.click(screen.getByRole('button', { name: 'Tạo hồ sơ bệnh nhân' }));
    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    expect(await screen.findByText('BN-2024-0001')).toBeInTheDocument();
  });

  it('đóng modal bằng nút Hủy bỏ', async () => {
    const { onClose } = setup();
    await userEvent.click(screen.getByRole('button', { name: 'Hủy bỏ' }));
    expect(onClose).toHaveBeenCalled();
  });
});
