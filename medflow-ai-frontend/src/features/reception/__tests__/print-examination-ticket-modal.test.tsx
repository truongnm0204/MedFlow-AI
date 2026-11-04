import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { AxiosError, type AxiosResponse, type InternalAxiosRequestConfig } from 'axios';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, describe, expect, it, vi } from 'vitest';

import { useAuthStore } from '@/features/auth/store/auth.store';

import * as visitTicketApi from '../api/visit-ticket.api';
import { buildFallbackExaminationTicket } from '../data/reception-mock-data';
import { ReceptionDashboardPage } from '../pages/reception-dashboard-page';

function renderReceptionDashboard() {
  useAuthStore.setState({
    user: {
      id: 5,
      email: 'reception01@medflow.test',
      fullName: 'Lễ tân Lê Lan',
      roles: ['RECEPTION'],
      patientCode: null,
    },
    accessToken: 'tok-rec',
    refreshToken: 'rtok-rec',
    isAuthenticated: true,
  });

  const qc = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  });

  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <ReceptionDashboardPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('PrintExaminationTicketModal & ReceptionDashboardPage (UC29)', () => {
  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('opens examination ticket modal from queue row and prints ticket via backend API', async () => {
    const initialTicket = {
      ...buildFallbackExaminationTicket('VIS-202610-0001'),
      ticketNo: 'A001',
      queueNumber: 1,
      queueNumberFormatted: '001',
      roomCode: 'RM-001',
      roomName: 'Khám Tiêu hóa 101',
      doctorCode: 'DOC-001',
      doctorName: 'BS Nguyễn Minh Đức',
      printCount: 0,
      isReprint: false,
    };

    const printedTicket = {
      ...initialTicket,
      copies: 2,
      printCount: 1,
      isReprint: false,
    };

    const fetchSpy = vi
      .spyOn(visitTicketApi, 'fetchExaminationTicket')
      .mockResolvedValue(initialTicket);
    const printSpy = vi
      .spyOn(visitTicketApi, 'printExaminationTicket')
      .mockResolvedValue(printedTicket);

    renderReceptionDashboard();

    const printRowBtn = screen.getByRole('button', {
      name: 'In phiếu khám cho Nguyễn Văn An',
    });
    fireEvent.click(printRowBtn);

    expect(
      await screen.findByRole('heading', { name: /In phiếu khám bệnh ngoại trú \(UC29\)/i }),
    ).toBeInTheDocument();

    await waitFor(() => {
      expect(fetchSpy).toHaveBeenCalledWith('VIS-202610-0001');
    });

    expect(await screen.findByText('A001')).toBeInTheDocument();
    expect(screen.getByText(/Khám Tiêu hóa 101/i)).toBeInTheDocument();
    expect(screen.getByText('BS Nguyễn Minh Đức')).toBeInTheDocument();

    const copiesInput = screen.getByLabelText(/Số bản in/i);
    fireEvent.change(copiesInput, { target: { value: '2' } });

    const reasonInput = screen.getByLabelText(/Ghi chú \/ Lý do in/i);
    fireEvent.change(reasonInput, { target: { value: 'In phiếu tại quầy 1' } });

    const submitPrintBtn = screen.getByRole('button', { name: /^In phiếu khám$/i });
    fireEvent.click(submitPrintBtn);

    await waitFor(() => {
      expect(printSpy).toHaveBeenCalledWith('VIS-202610-0001', {
        copies: 2,
        isReprint: false,
        reason: 'In phiếu tại quầy 1',
      });
    });

    expect(await screen.findByText(/Đã phát hành lệnh in phiếu khám A001/i)).toBeInTheDocument();
  });

  it('displays 409 Conflict alert when UC26 doctor or UC27 room prerequisite is missing', async () => {
    const initialTicket = buildFallbackExaminationTicket('VIS-202610-0001');
    vi.spyOn(visitTicketApi, 'fetchExaminationTicket').mockResolvedValue(initialTicket);

    const conflictErr = new AxiosError(
      'Conflict',
      'ERR_BAD_REQUEST',
      {} as InternalAxiosRequestConfig,
      {},
      {
        status: 409,
        statusText: 'Conflict',
        headers: {},
        config: {} as InternalAxiosRequestConfig,
        data: {
          status: 409,
          detail:
            'Lượt khám chưa được phân công bác sĩ phụ trách (UC26). Vui lòng gán bác sĩ trước khi in phiếu khám.',
        },
      } as AxiosResponse,
    );
    vi.spyOn(visitTicketApi, 'printExaminationTicket').mockRejectedValueOnce(conflictErr);

    renderReceptionDashboard();

    fireEvent.click(screen.getByRole('button', { name: /In phiếu khám bệnh/i }));

    await screen.findByLabelText('Phiếu khám bệnh ngoại trú');

    fireEvent.click(screen.getByRole('button', { name: /^In phiếu khám$/i }));

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent(/chưa được phân công bác sĩ phụ trách \(UC26\)/i);
  });
});
