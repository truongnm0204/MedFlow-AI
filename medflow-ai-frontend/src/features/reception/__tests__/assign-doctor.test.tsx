import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { AssignDoctorPanel } from '../components/assign-doctor-panel';
import { DoctorWorkloadCard } from '../components/doctor-workload-card';
import { ASSIGN_DOCTOR_MOCK_DOCTORS, ASSIGN_DOCTOR_VISIT_MOCK } from '../data/assign-doctor-mock';
import { VisitRoutingPage } from '../pages/visit-routing-page';

const assignedDoctorDto = {
  visitId: 1,
  visitCode: 'VIS-1',
  patient: {
    visitNumericId: 1,
    fullName: 'Trần Minh Đức',
    gender: 'MALE',
    age: 41,
    patientCode: 'BN-2024-0021',
    cccdVerified: true,
    serviceName: 'Khám Nội tổng quát',
    serviceCode: 'DV-01',
    price: 200000,
  },
  doctors: [
    {
      doctorId: 7,
      fullName: 'BS.CKI Trần Quốc Tuấn',
      departmentName: 'Nội tổng quát',
      department: 'Nội tổng quát',
      roomName: 'Phòng 204',
      roomLocation: 'Tầng 2',
      waitingCount: 2,
      currentTicket: 'A12',
      estimatedWait: '~10-15 phút',
      workloadLevel: 'LOW',
    },
  ],
};

function renderRoutingPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={['/reception/routing?visitId=VIS-1']}>
        <VisitRoutingPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

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

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('VisitRoutingPage loads assignable doctors and confirms assignment via API', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({ data: assignedDoctorDto } as never);
    const put = vi.spyOn(apiClient, 'put').mockResolvedValue({
      data: {
        visitCode: 'VIS-1',
        assignedDoctorName: 'BS.CKI Trần Quốc Tuấn',
        roomName: 'Phòng 204',
      },
    } as never);

    renderRoutingPage();

    const radio = await screen.findByRole('radio', { name: /Trần Quốc Tuấn/ });
    fireEvent.click(radio);
    fireEvent.click(screen.getByRole('button', { name: 'Xác nhận phân bổ bác sĩ' }));

    await waitFor(() => expect(put).toHaveBeenCalledTimes(1));
    expect(put.mock.calls[0]?.[0]).toBe('/reception/visits/VIS-1/doctor');
    expect(await screen.findByRole('status')).toHaveTextContent('Đã phân bổ BS.CKI Trần Quốc Tuấn');
  });
});
