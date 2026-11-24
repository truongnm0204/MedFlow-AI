import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';
import { DeactivateStaffModal } from '../components/deactivate-staff-modal';
import { StaffAccountListPage } from '../pages/staff-account-list-page';
import type { StaffTargetSummary } from '../types/deactivate-staff.types';

vi.mock('@/shared/lib/axios', () => ({
  apiClient: {
    get: vi.fn(),
    patch: vi.fn(),
  },
}));

const staff: StaffTargetSummary = {
  id: 'stf-0142',
  staffCode: 'STF-0142',
  fullName: 'Lê Hoàng Minh',
  role: 'Bác sĩ',
  department: 'Khoa Nội Tiêu hóa',
  email: 'minh.lh@medflow.vn',
  pendingAssignments: 0,
};

describe('DeactivateStaffModal (SCR-ADM-DOC-05)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders staff info and eligible dependency check', () => {
    render(<DeactivateStaffModal staff={staff} onCancel={vi.fn()} onConfirm={vi.fn()} />);
    expect(screen.getByRole('dialog')).toHaveAccessibleName('Ngừng hoạt động tài khoản nhân sự?');
    expect(screen.getByText('STF-0142')).toHaveClass('font-mono');
    expect(screen.getByText(/0 ca \(Đủ điều kiện vô hiệu hóa\)/)).toBeInTheDocument();
  });

  it('submits selected reason and trimmed note', () => {
    const onConfirm = vi.fn();
    render(<DeactivateStaffModal staff={staff} onCancel={vi.fn()} onConfirm={onConfirm} />);
    fireEvent.click(screen.getByLabelText('Tạm đình chỉ công tác'));
    fireEvent.change(screen.getByLabelText(/Ghi chú lý do/), { target: { value: '  vi phạm  ' } });
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận ngừng hoạt động' }));
    expect(onConfirm).toHaveBeenCalledWith({
      staffId: 'stf-0142',
      reason: 'SUSPENDED',
      note: 'vi phạm',
    });
  });

  it('blocks confirmation when assignments remain', () => {
    const onConfirm = vi.fn();
    render(
      <DeactivateStaffModal
        staff={{ ...staff, pendingAssignments: 3 }}
        onCancel={vi.fn()}
        onConfirm={onConfirm}
      />,
    );
    expect(screen.getByRole('alert')).toHaveTextContent('3 ca');
    const button = screen.getByRole('button', { name: 'Xác nhận ngừng hoạt động' });
    expect(button).toBeDisabled();
    fireEvent.click(button);
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it('cancels via button and Escape', () => {
    const onCancel = vi.fn();
    render(<DeactivateStaffModal staff={staff} onCancel={onCancel} onConfirm={vi.fn()} />);
    fireEvent.click(screen.getByRole('button', { name: 'Hủy bỏ' }));
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(onCancel).toHaveBeenCalledTimes(2);
  });

  it('calls PATCH /admin/staff/:id/deactivate and marks the account DISABLED from the list page after confirm', async () => {
    const rows = [
      {
        staffId: 142,
        code: 'STF-0142',
        fullName: 'Lê Hoàng Minh',
        email: 'minh.lh@medflow.vn',
        roleCode: 'DOCTOR',
        role: 'Bác sĩ',
        department: 'Khoa Nội Tiêu hóa',
        facility: 'MedFlow Cơ sở 1',
        status: 'ACTIVE',
      },
      {
        staffId: 200,
        code: 'STF-0200',
        fullName: 'Phạm Văn C',
        email: 'c.pv@medflow.vn',
        roleCode: 'NURSE',
        role: 'Điều dưỡng',
        department: 'Khoa Nhi',
        facility: 'MedFlow Cơ sở 1',
        status: 'DISABLED',
      },
    ];

    vi.mocked(apiClient.get).mockImplementation(async () => ({
      data: { data: rows },
    }));
    vi.mocked(apiClient.patch).mockImplementation(async () => {
      rows[0] = { ...rows[0], status: 'DISABLED' };
      return { data: {} };
    });

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter>
          <StaffAccountListPage />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(await screen.findByText('STF-0142')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Ngừng hoạt động Lê Hoàng Minh' }));
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận ngừng hoạt động' }));

    await waitFor(() => {
      expect(apiClient.patch).toHaveBeenCalledWith(
        '/admin/staff/142/deactivate',
        expect.objectContaining({ reason: 'RESIGNED' }),
      );
      expect(screen.getAllByText('DISABLED')).toHaveLength(2);
    });
    expect(screen.getByRole('button', { name: 'Ngừng hoạt động Lê Hoàng Minh' })).toBeDisabled();
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
