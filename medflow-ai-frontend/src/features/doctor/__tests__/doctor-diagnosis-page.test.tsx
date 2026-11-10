import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';

import { DoctorDiagnosisPage } from '../pages/doctor-diagnosis-page';

function renderDiagnosisPage() {
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
        <DoctorDiagnosisPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('DoctorDiagnosisPage (Issue #111)', () => {
  it('renders patient header, allergy alert, step 5 active and clinical evidence', () => {
    renderDiagnosisPage();

    expect(screen.getByText('NGUYỄN VĂN AN')).toBeInTheDocument();
    expect(screen.getByText('PAT-2024-0108')).toBeInTheDocument();
    expect(screen.getAllByText(/Penicillin, NSAIDs \(Aspirin\)/i).length).toBeGreaterThan(0);
    expect(screen.getByText('Nội soi dạ dày')).toBeInTheDocument();
    expect(screen.getByText('Siêu âm ổ bụng')).toBeInTheDocument();
    expect(screen.getByText('Huyết học (CBC)')).toBeInTheDocument();
  });

  it('shows validation errors when submitting without primary diagnosis or valid conclusion', () => {
    renderDiagnosisPage();

    fireEvent.click(screen.getByRole('button', { name: /Hoàn tất ca khám & Phát hành Kết luận/i }));

    expect(
      screen.getByText(/Vui lòng chọn chẩn đoán bệnh chính \(ICD-10\)\./i),
    ).toBeInTheDocument();
    expect(screen.getByText(/Kết luận lâm sàng phải có tối thiểu 10 ký tự\./i)).toBeInTheDocument();
  });

  it('selects primary and secondary ICD-10 diagnoses and submits successfully', () => {
    renderDiagnosisPage();

    // Select primary ICD-10
    const primaryInput = screen.getByLabelText(/Chọn bệnh chính \(ICD-10\)/i);
    fireEvent.focus(primaryInput);
    fireEvent.change(primaryInput, { target: { value: 'K29.5' } });
    fireEvent.click(screen.getByRole('option', { name: /K29\.5/i }));

    expect(screen.getByTestId('primary-diagnosis')).toHaveTextContent('K29.5');

    // Select secondary ICD-10
    const secondaryInput = screen.getByLabelText(/Thêm bệnh kèm theo \(ICD-10\)/i);
    fireEvent.focus(secondaryInput);
    fireEvent.change(secondaryInput, { target: { value: 'K21.0' } });
    fireEvent.click(screen.getByRole('option', { name: /K21\.0/i }));

    // Fill clinical conclusion
    const conclusionArea = screen.getByLabelText(/Kết luận lâm sàng/i);
    fireEvent.change(conclusionArea, {
      target: {
        value: 'Viêm dạ dày mạn tính thể teo kèm HP dương tính, điều trị phác đồ 14 ngày.',
      },
    });

    // Submit
    fireEvent.click(screen.getByRole('button', { name: /Hoàn tất ca khám & Phát hành Kết luận/i }));

    expect(
      screen.getByText(/Đã phát hành chẩn đoán chính thức \(K29\.5\) và hoàn tất ca khám/i),
    ).toBeInTheDocument();
    expect(screen.getByText(/Đã phát hành kết luận \(Completed\)/i)).toBeInTheDocument();
  });

  it('shows draft saved notice when clicking Lưu nháp', () => {
    renderDiagnosisPage();

    fireEvent.click(screen.getByRole('button', { name: /Lưu nháp/i }));
    expect(screen.getByText(/Đã lưu bản nháp chẩn đoán cho lượt khám/i)).toBeInTheDocument();
  });
});
