import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import { DepartmentTable } from '../components/department-table';
import type { DepartmentItem } from '../types/department.types';

const mockDepartments: DepartmentItem[] = [
  {
    id: 'dep-001',
    code: 'DEP-001',
    name: 'Khoa Nội Tiêu hóa',
    facility: 'MedFlow Cơ sở 1',
    status: 'ACTIVE',
    activeRoomsCount: 2,
    activeDoctorsCount: 3,
    hasActiveDependencies: true,
  },
  {
    id: 'dep-013',
    code: 'DEP-013',
    name: 'Khoa Da liễu',
    facility: 'MedFlow Cơ sở 1',
    status: 'INACTIVE',
    activeRoomsCount: 0,
    activeDoctorsCount: 0,
    hasActiveDependencies: false,
  },
];

describe('DepartmentTable', () => {
  it('renders all required column headers', () => {
    render(
      <MemoryRouter>
        <DepartmentTable
          departments={mockDepartments}
          totalCount={mockDepartments.length}
          page={1}
          pageSize={10}
          onPageChange={vi.fn()}
        />
      </MemoryRouter>,
    );

    for (const col of ['MÃ KHOA', 'TÊN KHOA', 'CƠ SỞ', 'TRẠNG THÁI', 'THAO TÁC']) {
      expect(screen.getByRole('columnheader', { name: col })).toBeInTheDocument();
    }
  });

  it('renders action buttons including View and Status toggle', () => {
    render(
      <MemoryRouter>
        <DepartmentTable
          departments={mockDepartments}
          totalCount={mockDepartments.length}
          page={1}
          pageSize={10}
          onPageChange={vi.fn()}
        />
      </MemoryRouter>,
    );

    expect(
      screen.getByRole('button', { name: 'Xem chi tiết Khoa Nội Tiêu hóa' }),
    ).toBeInTheDocument();
    expect(
      screen.getByRole('button', { name: 'Ngừng hoạt động Khoa Nội Tiêu hóa' }),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Kích hoạt lại Khoa Da liễu' })).toBeInTheDocument();
  });

  it('calls onRequestDeactivate when clicking power button on an active department', () => {
    const onRequestDeactivate = vi.fn();
    render(
      <MemoryRouter>
        <DepartmentTable
          departments={mockDepartments}
          totalCount={mockDepartments.length}
          page={1}
          pageSize={10}
          onPageChange={vi.fn()}
          onRequestDeactivate={onRequestDeactivate}
        />
      </MemoryRouter>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Ngừng hoạt động Khoa Nội Tiêu hóa' }));
    expect(onRequestDeactivate).toHaveBeenCalledWith(mockDepartments[0]);
  });

  it('calls onToggleStatus when clicking power button on an inactive department', () => {
    const onToggleStatus = vi.fn();
    render(
      <MemoryRouter>
        <DepartmentTable
          departments={mockDepartments}
          totalCount={mockDepartments.length}
          page={1}
          pageSize={10}
          onPageChange={vi.fn()}
          onToggleStatus={onToggleStatus}
        />
      </MemoryRouter>,
    );

    fireEvent.click(screen.getByRole('button', { name: 'Kích hoạt lại Khoa Da liễu' }));
    expect(onToggleStatus).toHaveBeenCalledWith('dep-013');
  });

  it('renders empty message when no departments match', () => {
    render(
      <MemoryRouter>
        <DepartmentTable
          departments={[]}
          totalCount={0}
          page={1}
          pageSize={10}
          onPageChange={vi.fn()}
        />
      </MemoryRouter>,
    );

    expect(screen.getByText('Không tìm thấy khoa phòng phù hợp')).toBeInTheDocument();
  });
});
