import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { DiagnosticResultsTrackingPage } from '../pages/diagnostic-results-tracking-page';

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <DiagnosticResultsTrackingPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DiagnosticResultsTrackingPage', () => {
  it('render 4 stat cards', () => {
    renderPage();
    expect(screen.getByText('TỔNG LƯỢT CHỈ ĐỊNH CLS')).toBeInTheDocument();
    expect(screen.getByText('HOÀN TẤT 100%')).toBeInTheDocument();
    expect(screen.getByText('ĐANG CHỜ KẾT QUẢ')).toBeInTheDocument();
    expect(screen.getByText('CẢNH BÁO BẤT THƯỜNG')).toBeInTheDocument();
    expect(within(screen.getByTestId('tracking-stat-totalOrders')).getByText('28')).toBeVisible();
    expect(within(screen.getByTestId('tracking-stat-waiting')).getByText('09')).toBeVisible();
    expect(within(screen.getByTestId('tracking-stat-abnormal')).getByText('04')).toBeVisible();
  });

  it('loc theo tu khoa tim kiem', () => {
    renderPage();
    const input = screen.getByLabelText('Tìm kiếm lượt khám CLS');
    fireEvent.change(input, { target: { value: 'BN-000103' } });
    expect(screen.getByText('VIS-2026-0103')).toBeInTheDocument();
    expect(screen.queryByText('VIS-2026-0101')).not.toBeInTheDocument();

    fireEvent.change(input, { target: { value: 'khong-ton-tai' } });
    expect(screen.getByText('Không tìm thấy lượt khám phù hợp với bộ lọc.')).toBeInTheDocument();
  });

  it('loc theo trang thai hoan tat va canh bao bat thuong', () => {
    renderPage();
    fireEvent.click(screen.getByLabelText('Lọc Hoàn tất 100%'));
    expect(screen.getByText('VIS-2026-0101')).toBeInTheDocument();
    expect(screen.queryByText('VIS-2026-0102')).not.toBeInTheDocument();

    fireEvent.click(screen.getByLabelText('Lọc Có cảnh báo bất thường'));
    expect(screen.getByText('VIS-2026-0102')).toBeInTheDocument();
    expect(screen.getByText('VIS-2026-0103')).toBeInTheDocument();
    expect(screen.queryByText('VIS-2026-0101')).not.toBeInTheDocument();
  });

  it('mo va dong modal tong hop ket qua cua luot kham', () => {
    renderPage();
    expect(screen.getByText('2/3')).toBeInTheDocument();
    fireEvent.click(screen.getByLabelText('Xem tổng hợp KQ VIS-2026-0102'));

    const dialog = screen.getByRole('dialog');
    expect(within(dialog).getByText('Siêu âm ổ bụng')).toBeInTheDocument();
    expect(within(dialog).getAllByText('SIGNED').length).toBe(2);
    expect(within(dialog).getByText('IN_PROGRESS')).toBeInTheDocument();
    expect(within(dialog).getByText('Glucose 11.8 mmol/L (cao).')).toBeInTheDocument();

    fireEvent.click(screen.getByLabelText('Đóng tổng hợp kết quả'));
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });
});
