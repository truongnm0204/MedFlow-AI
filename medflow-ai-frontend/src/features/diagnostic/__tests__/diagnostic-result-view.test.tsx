import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';

import { DiagnosticResultViewPage } from '../pages/diagnostic-result-view-page';

function renderResultViewPage() {
  useAuthStore.setState({
    user: {
      id: 4,
      email: 'ktv@medflow.test',
      fullName: 'BS.CKI Hoàng Minh Hải',
      roles: ['DIAGNOSTIC_STAFF'],
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
        <DiagnosticResultViewPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DiagnosticResultViewPage (Issue #137)', () => {
  it('renders patient header, order metadata and allergy warning', () => {
    renderResultViewPage();

    expect(screen.getByText('NGUYỄN VĂN AN')).toBeInTheDocument();
    expect(screen.getByText('PAT-2024-0108')).toBeInTheDocument();
    expect(screen.getAllByText('ORD-2026-0415-0142').length).toBeGreaterThan(0);
    expect(screen.getByText(/CẢNH BÁO DỊ ỨNG: DỊ ỨNG PENICILLIN/i)).toBeInTheDocument();
  });

  it('renders 4 imaging frames, microbiology test result and PACS video', () => {
    renderResultViewPage();

    const grid = screen.getByTestId('imaging-grid');
    expect(grid.querySelectorAll('figure').length).toBe(4);
    expect(screen.getByText(/ÂM TÍNH \(NEGATIVE\)/i)).toBeInTheDocument();
    expect(screen.getByText('REC_20260415_1015_0108.mp4')).toBeInTheDocument();
  });

  it('renders structured anatomical report, conclusion and ICD-10 tags', () => {
    renderResultViewPage();

    expect(screen.getByText(/1\. Thực quản \(Esophagus\)/i)).toBeInTheDocument();
    expect(screen.getByText(/2\. Dạ dày \(Stomach\)/i)).toBeInTheDocument();
    expect(screen.getByText(/3\. Tá tràng \(Duodenum/i)).toBeInTheDocument();
    expect(screen.getByText(/Viêm sung huyết, trợt nông niêm mạc hang vị/i)).toBeInTheDocument();
    expect(screen.getByText('K29.6')).toBeInTheDocument();
    expect(screen.getByText('K29.5')).toBeInTheDocument();
  });

  it('triggers PACS download, A4 preview and print action', () => {
    const printSpy = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    renderResultViewPage();

    fireEvent.click(screen.getByRole('button', { name: /Tải tệp lưu trữ PACS/i }));
    expect(screen.getByText(/Đang tải gói dữ liệu PACS/i)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /Xem trước in A4/i }));
    expect(screen.getByText(/Đã xuất bản xem trước khổ A4/i)).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /In phiếu kết quả/i }));
    expect(printSpy).toHaveBeenCalled();

    printSpy.mockRestore();
  });
});
