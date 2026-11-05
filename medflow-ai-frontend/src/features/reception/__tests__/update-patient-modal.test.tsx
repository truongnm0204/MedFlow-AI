import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';

import { UpdatePatientModal } from '../components/update-patient-modal';
import { PATIENT_UPDATE_MOCK } from '../data/reception-mock-data';

function setup() {
  const onClose = vi.fn();
  const onSubmit = vi.fn();
  render(
    <UpdatePatientModal open patient={PATIENT_UPDATE_MOCK} onClose={onClose} onSubmit={onSubmit} />,
  );
  return { onClose, onSubmit, user: userEvent.setup() };
}

describe('UpdatePatientModal', () => {
  it('prefill du lieu benh nhan', () => {
    setup();
    expect(screen.getByTestId('patient-code')).toHaveTextContent('BN-2024-0018');
    expect(screen.getByLabelText('Họ và tên')).toHaveValue('Nguyễn Văn An');
    expect(screen.getByLabelText('Ngày sinh')).toHaveValue('1985-03-12');
    expect(screen.getByLabelText('Giới tính')).toHaveValue('MALE');
    expect(screen.getByLabelText('Số CCCD/VNeID')).toHaveValue('079085001234');
    expect(screen.getByLabelText('Số điện thoại')).toHaveValue('0912345678');
    expect(screen.getByLabelText('Nơi ĐKKCB ban đầu')).toHaveValue('Bệnh viện Quận 1');
  });

  it('hien loi validation khi du lieu sai', async () => {
    const { user, onSubmit } = setup();
    const name = screen.getByLabelText('Họ và tên');
    await user.clear(name);
    await user.type(name, 'A');
    const cccd = screen.getByLabelText('Số CCCD/VNeID');
    await user.clear(cccd);
    await user.type(cccd, '123');
    const phone = screen.getByLabelText('Số điện thoại');
    await user.clear(phone);
    await user.type(phone, '0123456789');
    await user.clear(screen.getByLabelText('Địa chỉ thường trú'));
    await user.click(screen.getByRole('button', { name: 'Cập nhật hồ sơ' }));

    expect(await screen.findByText('Họ và tên tối thiểu 2 ký tự')).toBeInTheDocument();
    expect(screen.getByText('Số CCCD/VNeID phải gồm 12 chữ số')).toBeInTheDocument();
    expect(screen.getByText(/Số điện thoại phải gồm 10 chữ số/)).toBeInTheDocument();
    expect(screen.getByText('Vui lòng nhập địa chỉ thường trú')).toBeInTheDocument();
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('submit thanh cong voi gia tri da chinh sua', async () => {
    const { user, onSubmit } = setup();
    const name = screen.getByLabelText('Họ và tên');
    await user.clear(name);
    await user.type(name, 'Nguyễn Văn Bình');
    await user.selectOptions(screen.getByLabelText('Giới tính'), 'OTHER');
    await user.click(screen.getByRole('button', { name: 'Cập nhật hồ sơ' }));

    await waitFor(() => expect(onSubmit).toHaveBeenCalledTimes(1));
    const [code, values] = onSubmit.mock.calls[0] as [string, Record<string, string>];
    expect(code).toBe('BN-2024-0018');
    expect(values.fullName).toBe('Nguyễn Văn Bình');
    expect(values.gender).toBe('OTHER');
  });

  it('dong modal khi bam Huy bo', async () => {
    const { user, onClose } = setup();
    await user.click(screen.getByRole('button', { name: 'Hủy bỏ' }));
    expect(onClose).toHaveBeenCalled();
  });
});
