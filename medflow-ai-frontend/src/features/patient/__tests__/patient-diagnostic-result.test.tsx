import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';

import { PatientDiagnosticResultPage } from '../pages/patient-diagnostic-result-page';

function renderPatientResultPage() {
  useAuthStore.setState({
    user: {
      id: 10,
      email: 'patient@medflow.test',
      fullName: 'Nguyễn Văn An',
      roles: ['PATIENT'],
      patientCode: 'PAT-2024-0108',
    },
    accessToken: 'tok',
    refreshToken: 'rtok',
    isAuthenticated: true,
  });

  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <PatientDiagnosticResultPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('PatientDiagnosticResultPage (Issue #559)', () => {
  it('renders hospital letterhead, patient details, 4 diagnostic images and CBC lab table', () => {
    renderPatientResultPage();

    expect(screen.getByText('BỆNH VIỆN ĐA KHOA QUỐC TẾ MEDFLOW')).toBeInTheDocument();
    expect(screen.getByText('PHIẾU KẾT QUẢ CẬN LÂM SÀNG')).toBeInTheDocument();
    expect(screen.getByText('NGUYỄN VĂN AN')).toBeInTheDocument();
    expect(screen.getByTestId('patient-cls-images').querySelectorAll('figure').length).toBe(4);
    expect(screen.getByText('Bạch cầu (WBC)')).toBeInTheDocument();
    expect(screen.getByText('ĐÃ KÝ SỐ ĐIỆN TỬ HỢP LỆ')).toBeInTheDocument();
  });

  it('triggers PDF download notice and window.print() action', () => {
    const printSpy = vi.spyOn(window, 'print').mockImplementation(() => undefined);
    renderPatientResultPage();

    fireEvent.click(screen.getByRole('button', { name: /Tải file PDF/i }));
    expect(
      screen.getByText(/Đang tải xuống tài liệu PDF: KQ-CLS-VIS-2026-0415-0108\.pdf/i),
    ).toBeInTheDocument();

    fireEvent.click(screen.getByRole('button', { name: /In phiếu kết quả/i }));
    expect(printSpy).toHaveBeenCalled();

    printSpy.mockRestore();
  });
});
