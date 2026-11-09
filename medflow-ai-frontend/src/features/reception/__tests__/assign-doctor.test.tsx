import { fireEvent, render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';

import { AssignDoctorPanel } from '../components/assign-doctor-panel';
import { DoctorWorkloadCard } from '../components/doctor-workload-card';
import { ASSIGN_DOCTOR_MOCK_DOCTORS, ASSIGN_DOCTOR_VISIT_MOCK } from '../data/assign-doctor-mock';
import { VisitRoutingPage } from '../pages/visit-routing-page';

describe('Assign Doctor (Issue #77)', () => {
  it('DoctorWorkloadCard renders workload info and handles select', () => {
    const onSelect = vi.fn();
    render(
      <DoctorWorkloadCard
        doctor={ASSIGN_DOCTOR_MOCK_DOCTORS[0]}
        selected={false}
        onSelect={onSelect}
      />,
    );
    expect(screen.getByText('ThS.BS Nguyễn Văn Hùng')).toBeInTheDocument();
    expect(screen.getByText(/Tải thấp \(Đang chờ 2\)/)).toBeInTheDocument();
    expect(screen.getByText(/~10-15 phút/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('radio'));
    expect(onSelect).toHaveBeenCalledWith('doc-001');
  });

  it('AssignDoctorPanel disables confirm until a doctor is selected', () => {
    const onConfirm = vi.fn();
    render(
      <AssignDoctorPanel
        patient={ASSIGN_DOCTOR_VISIT_MOCK}
        doctors={ASSIGN_DOCTOR_MOCK_DOCTORS}
        selectedDoctorId={null}
        onSelectDoctor={vi.fn()}
        onConfirm={onConfirm}
      />,
    );
    expect(screen.getByText('Trần Minh Đức')).toBeInTheDocument();
    expect(screen.getByText(/BN-2024-0021/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Xác nhận phân bổ bác sĩ' })).toBeDisabled();
  });

  it('VisitRoutingPage selects a doctor and confirms assignment', () => {
    render(
      <MemoryRouter>
        <VisitRoutingPage />
      </MemoryRouter>,
    );
    fireEvent.click(screen.getByRole('radio', { name: /Trần Quốc Tuấn/ }));
    expect(screen.getByTestId('selected-summary')).toHaveTextContent('Phòng 204');
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận phân bổ bác sĩ' }));
    expect(screen.getByRole('status')).toHaveTextContent('Đã phân bổ BS.CKI Trần Quốc Tuấn');
  });
});
