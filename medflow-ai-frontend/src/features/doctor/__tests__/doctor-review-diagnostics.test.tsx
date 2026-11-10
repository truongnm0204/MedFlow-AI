import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';

import { DoctorReviewDiagnosticsPage } from '../pages/doctor-review-diagnostics-page';

function renderReviewPage() {
  useAuthStore.setState({
    user: {
      id: 3,
      email: 'bacsi@medflow.test',
      fullName: 'BS. CKII Trần Quốc Tuấn',
      roles: ['DOCTOR'],
      patientCode: null,
    },
    accessToken: 'tok',
    refreshToken: 'rtok',
    isAuthenticated: true,
  });

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <DoctorReviewDiagnosticsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DoctorReviewDiagnosticsPage (Issue #113)', () => {
  it('renders patient header, allergy alert, step 4 active and readiness banner', () => {
    renderReviewPage();

    expect(screen.getByText('NGUYỄN VĂN AN')).toBeInTheDocument();
    expect(screen.getByText('PAT-2024-0108')).toBeInTheDocument();
    expect(screen.getByText(/Penicillin, NSAIDs/)).toBeInTheDocument();
    expect(
      screen.getByText(/TẤT CẢ KẾT QUẢ ĐÃ SẴN SÀNG \(3\/3 DỊCH VỤ HOÀN TẤT\)/i),
    ).toBeInTheDocument();
  });

  it('renders all 3 diagnostic cards and displays detail panel for default endoscopy order', () => {
    renderReviewPage();

    expect(screen.getByTestId('diagnostic-result-card-CLS-ENDO-01')).toBeInTheDocument();
    expect(screen.getByTestId('diagnostic-result-card-CLS-US-02')).toBeInTheDocument();
    expect(screen.getByTestId('diagnostic-result-card-CLS-LAB-01')).toBeInTheDocument();
    expect(screen.getByText('HP DƯƠNG TÍNH (+)')).toBeInTheDocument();

    const detailPanel = screen.getByTestId('diagnostic-result-detail-panel');
    expect(detailPanel).toHaveTextContent('CLS-ENDO-01');
    expect(detailPanel).toHaveTextContent('BS.CKI Hoàng Minh Hải');
    expect(screen.getByTestId('diagnostic-image-grid')).toBeInTheDocument();
  });

  it('switches detail panel when clicking another diagnostic card', () => {
    renderReviewPage();

    const viewUsBtn = screen.getByRole('button', {
      name: /Xem chi tiết phiếu KQ CLS-US-02/i,
    });
    fireEvent.click(viewUsBtn);

    const detailPanel = screen.getByTestId('diagnostic-result-detail-panel');
    expect(detailPanel).toHaveTextContent('CLS-US-02');
    expect(detailPanel).toHaveTextContent('ThS.BS Nguyễn Thu Trang');
    expect(detailPanel).toHaveTextContent('Gan nhiễm mỡ độ I');
  });

  it('triggers download PDF notice, portal share notice, and print action', () => {
    const printSpy = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    renderReviewPage();

    fireEvent.click(screen.getByRole('button', { name: /Tải file PDF kết quả gốc/i }));
    expect(
      screen.getByText(/Đang tải file PDF kết quả gốc: KQ-CLS-ENDO-01\.pdf/i),
    ).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Chia sẻ portal bệnh nhân/i }));
    expect(
      screen.getByText(/Đã chia sẻ phiếu CLS-ENDO-01 lên portal bệnh nhân\./i),
    ).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /In toàn bộ phiếu kết quả/i }));
    expect(printSpy).toHaveBeenCalled();

    printSpy.mockRestore();
  });
});
