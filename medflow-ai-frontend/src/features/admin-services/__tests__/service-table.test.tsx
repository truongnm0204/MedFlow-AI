import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import { ServiceTable } from '../components/service-table';
import type { ExaminationService } from '../types/examination-service.types';

const mockServices: ExaminationService[] = [
  {
    id: 'EX-1-01',
    code: 'EX-1-01',
    name: 'Khám Nội Tổng quát & Tư vấn Sức khỏe',
    description: 'Đánh giá sức khỏe ban đầu',
    category: 'Khám Lâm sàng',
    department: 'Khoa Khám bệnh',
    price: 300000,
    onlineBooking: true,
    status: 'ACTIVE',
  },
  {
    id: 'EX-2-02',
    code: 'EX-2-02',
    name: 'Khám Chuyên khoa Nội Tiêu hóa & Gan Mật',
    description: 'Khám chuyên sâu bệnh lý tiêu hóa',
    category: 'Khám Lâm sàng',
    department: 'Khoa Nội Tiêu hóa',
    price: 350000,
    onlineBooking: false,
    status: 'SUSPENDED',
  },
];

describe('ServiceTable (SCR-ADM-ESV-01)', () => {
  it('renders table columns and status badges matching specifications', () => {
    render(
      <MemoryRouter>
        <ServiceTable services={mockServices} onViewHistory={vi.fn()} />
      </MemoryRouter>,
    );

    expect(screen.getByText('MÃ DỊCH VỤ')).toBeInTheDocument();
    expect(screen.getByText('TÊN DỊCH VỤ KHÁM NGOẠI TRÚ')).toBeInTheDocument();
    expect(screen.getByText('TRẠNG THÁI')).toBeInTheDocument();
    expect(screen.getByText('THAO TÁC')).toBeInTheDocument();
    expect(screen.getByText('Hoạt động')).toBeInTheDocument();
    expect(screen.getByText('Tạm dừng')).toBeInTheDocument();
  });

  it('triggers onRequestDeactivate when clicking Tạm ngừng dịch vụ in action menu', () => {
    const onRequestDeactivate = vi.fn();
    render(
      <MemoryRouter>
        <ServiceTable
          services={mockServices}
          onViewHistory={vi.fn()}
          onRequestDeactivate={onRequestDeactivate}
        />
      </MemoryRouter>,
    );

    // Open MoreVertical menu for the active service EX-1-01
    const moreBtn = screen.getByRole('button', {
      name: 'Thao tác Khám Nội Tổng quát & Tư vấn Sức khỏe',
    });
    fireEvent.click(moreBtn);

    // Click "Tạm ngừng dịch vụ"
    const deactivateBtn = screen.getByRole('button', {
      name: 'Ngừng hoạt động Khám Nội Tổng quát & Tư vấn Sức khỏe',
    });
    fireEvent.click(deactivateBtn);

    expect(onRequestDeactivate).toHaveBeenCalledWith(mockServices[0]);
  });

  it('triggers onToggleStatus when clicking Kích hoạt lại for suspended service', () => {
    const onToggleStatus = vi.fn();
    render(
      <MemoryRouter>
        <ServiceTable
          services={mockServices}
          onViewHistory={vi.fn()}
          onToggleStatus={onToggleStatus}
        />
      </MemoryRouter>,
    );

    // Open MoreVertical menu for the suspended service EX-2-02
    const moreBtn = screen.getByRole('button', {
      name: 'Thao tác Khám Chuyên khoa Nội Tiêu hóa & Gan Mật',
    });
    fireEvent.click(moreBtn);

    // Click "Kích hoạt dịch vụ"
    const activateBtn = screen.getByRole('button', {
      name: 'Kích hoạt lại Khám Chuyên khoa Nội Tiêu hóa & Gan Mật',
    });
    fireEvent.click(activateBtn);

    expect(onToggleStatus).toHaveBeenCalledWith('EX-2-02');
  });

  it('triggers onRequestDeactivate when clicking Power button for active service', () => {
    const onRequestDeactivate = vi.fn();
    render(
      <MemoryRouter>
        <ServiceTable
          services={mockServices}
          onViewHistory={vi.fn()}
          onRequestDeactivate={onRequestDeactivate}
        />
      </MemoryRouter>,
    );

    const powerBtn = screen.getByLabelText('Đổi trạng thái Khám Nội Tổng quát & Tư vấn Sức khỏe');
    fireEvent.click(powerBtn);

    expect(onRequestDeactivate).toHaveBeenCalledWith(mockServices[0]);
  });

  it('renders empty message when no services are provided', () => {
    render(
      <MemoryRouter>
        <ServiceTable services={[]} onViewHistory={vi.fn()} />
      </MemoryRouter>,
    );

    expect(screen.getByText('Không tìm thấy dịch vụ khám phù hợp với bộ lọc.')).toBeInTheDocument();
  });
});
