import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import { DoctorTable } from '../components/doctor-table';
import type { Doctor } from '../types/doctor.types';

const mockDoctors: Doctor[] = [
  {
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
    scope: 'Khám bệnh chuyên khoa',
    biography: 'Tiểu sử bác sĩ Minh',
    active: true,
    history: [],
  },
  {
    id: 'DOC-0095',
    staffId: 'NV-1095',
    name: 'BSCKII Vũ Thị Bích Lan',
    email: 'lan.vtb@medflow.vn',
    degree: 'BSCKII',
    specialty: 'Tai Mũi Họng',
    department: 'Khoa Tai Mũi Họng',
    license: '003214/BYT-CCHN',
    room: 'PK-105',
    phone: '0988.777.666',
    experience: 15,
    scope: 'Khám TMH',
    biography: 'Tiểu sử bác sĩ Lan',
    active: false,
    history: [],
  },
];

describe('DoctorTable (SCR-ADM-DOC-01)', () => {
  it('renders columns with TRẠNG THÁI HỒ SƠ and THAO TÁC matching Figma', () => {
    render(
      <MemoryRouter>
        <DoctorTable
          doctors={mockDoctors}
          selectedIds={[]}
          onToggleSelection={vi.fn()}
          onToggleAll={vi.fn()}
          onToggleStatus={vi.fn()}
        />
      </MemoryRouter>,
    );

    expect(screen.getByText('TRẠNG THÁI HỒ SƠ')).toBeInTheDocument();
    expect(screen.getByText('THAO TÁC')).toBeInTheDocument();
    expect(screen.getByText('Đang hoạt động')).toBeInTheDocument();
    expect(screen.getByText('Ngừng hoạt động')).toBeInTheDocument();
  });

  it('triggers onRequestDeactivate when clicking Ngừng hoạt động in action menu', () => {
    const onRequestDeactivate = vi.fn();
    render(
      <MemoryRouter>
        <DoctorTable
          doctors={mockDoctors}
          selectedIds={[]}
          onToggleSelection={vi.fn()}
          onToggleAll={vi.fn()}
          onToggleStatus={vi.fn()}
          onRequestDeactivate={onRequestDeactivate}
        />
      </MemoryRouter>,
    );

    // Open MoreVertical menu for the active doctor DOC-0142
    const moreBtn = screen.getByRole('button', { name: 'Thao tác BS. CKI Lê Hoàng Minh' });
    fireEvent.click(moreBtn);

    // Click "Ngừng hoạt động"
    const deactivateBtn = screen.getByRole('button', {
      name: 'Ngừng hoạt động BS. CKI Lê Hoàng Minh',
    });
    fireEvent.click(deactivateBtn);

    expect(onRequestDeactivate).toHaveBeenCalledWith(mockDoctors[0]);
  });

  it('triggers onToggleStatus when clicking Kích hoạt lại for inactive doctor', () => {
    const onToggleStatus = vi.fn();
    render(
      <MemoryRouter>
        <DoctorTable
          doctors={mockDoctors}
          selectedIds={[]}
          onToggleSelection={vi.fn()}
          onToggleAll={vi.fn()}
          onToggleStatus={onToggleStatus}
        />
      </MemoryRouter>,
    );

    // Open MoreVertical menu for the inactive doctor DOC-0095
    const moreBtn = screen.getByRole('button', { name: 'Thao tác BSCKII Vũ Thị Bích Lan' });
    fireEvent.click(moreBtn);

    // Click "Kích hoạt lại"
    const activateBtn = screen.getByRole('button', {
      name: 'Kích hoạt lại BSCKII Vũ Thị Bích Lan',
    });
    fireEvent.click(activateBtn);

    expect(onToggleStatus).toHaveBeenCalledWith('DOC-0095');
  });

  it('renders empty message when no doctors are provided', () => {
    render(
      <MemoryRouter>
        <DoctorTable
          doctors={[]}
          selectedIds={[]}
          onToggleSelection={vi.fn()}
          onToggleAll={vi.fn()}
          onToggleStatus={vi.fn()}
        />
      </MemoryRouter>,
    );

    expect(screen.getByText('Không tìm thấy bác sĩ phù hợp.')).toBeInTheDocument();
  });
});
