import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import type { AxiosResponse } from 'axios';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { apiClient } from '@/shared/lib/axios';

import { OperationalReportsPage } from '../pages/operational-reports-page';

const SAMPLE_REPORT_DTO = {
  hospitalId: 1,
  facilityCode: 'HOS-01',
  facilityName: 'Bệnh viện MedFlow Hà Nội',
  fromDate: '2026-11-18',
  toDate: '2026-11-24',
  periodLabel: '18/11/2026 - 24/11/2026',
  selectedDepartmentId: null,
  selectedServiceId: null,
  generatedAtUtc: '2026-11-24T10:00:00Z',
  summary: {
    totalVisits: 45,
    totalAppointments: 50,
    completedVisits: 40,
    inProgressVisits: 3,
    waitingVisits: 2,
    walkInVisits: 12,
    noShowAppointments: 4,
    completionRatePercent: 88.9,
    averageWaitMinutes: 14.5,
    medianWaitMinutes: 12.0,
  },
  dailyVisits: [],
  hourlyIntake: [
    { hour: '08:00', count: 18, isPeak: true },
    { hour: '09:00', count: 12, isPeak: false },
  ],
  departmentVisits: [
    { departmentId: 10, departmentCode: 'DEP-GI', departmentName: 'Nội Tiêu Hóa', count: 25 },
    { departmentId: 11, departmentCode: 'DEP-CAR', departmentName: 'Tim Mạch', count: 20 },
  ],
  visitStatusSegments: [
    { key: 'COMPLETED', label: 'Hoàn tất khám', count: 40, percentValue: 88.9 },
    { key: 'IN_PROGRESS', label: 'Đang khám', count: 3, percentValue: 6.7 },
    { key: 'WAITING', label: 'Chờ khám', count: 2, percentValue: 4.4 },
  ],
  departmentRows: [
    {
      id: 'rpt-dept-10',
      code: 'RPT-DEP-GI',
      name: 'Tổng hợp vận hành Nội Tiêu Hóa',
      departmentId: 10,
      departmentName: 'Nội Tiêu Hóa',
      facilityName: 'Bệnh viện MedFlow Hà Nội',
      periodLabel: '18/11/2026 - 24/11/2026',
      totalVisits: 25,
      completedVisits: 22,
      walkInVisits: 6,
      totalAppointments: 28,
      noShowAppointments: 1,
      averageWaitMinutes: 13.0,
      medianWaitMinutes: 11.5,
      status: 'NEEDS_REVIEW',
    },
    {
      id: 'rpt-dept-11',
      code: 'RPT-DEP-CAR',
      name: 'Tổng hợp vận hành Tim Mạch',
      departmentId: 11,
      departmentName: 'Tim Mạch',
      facilityName: 'Bệnh viện MedFlow Hà Nội',
      periodLabel: '18/11/2026 - 24/11/2026',
      totalVisits: 20,
      completedVisits: 18,
      walkInVisits: 6,
      totalAppointments: 22,
      noShowAppointments: 0,
      averageWaitMinutes: 16.0,
      medianWaitMinutes: 13.0,
      status: 'COMPLETED',
    },
  ],
  availableDepartments: [
    { id: 10, code: 'DEP-GI', name: 'Nội Tiêu Hóa' },
    { id: 11, code: 'DEP-CAR', name: 'Tim Mạch' },
  ],
};

function renderPage() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter>
        <OperationalReportsPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('OperationalReportsPage (UC56 real API)', () => {
  beforeEach(() => {
    vi.spyOn(apiClient, 'get').mockResolvedValue({
      data: SAMPLE_REPORT_DTO,
    } as AxiosResponse);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('fetches GET /admin/reports/operational and renders real KPIs, charts, and department rows without 22/10/2026 mock', async () => {
    renderPage();

    expect(await screen.findByText('45')).toBeInTheDocument();
    expect(screen.getAllByText('88.9%').length).toBeGreaterThan(0);
    expect(screen.getByText('Cao điểm 08:00 · 18 lượt')).toBeInTheDocument();
    expect(screen.getByText('RPT-DEP-GI')).toBeInTheDocument();
    expect(screen.getByText('RPT-DEP-CAR')).toBeInTheDocument();
    expect(screen.queryByText(/22\/10\/2026/)).not.toBeInTheDocument();
    expect(screen.queryByText('326')).not.toBeInTheDocument();
  });

  it('renders — instead of 0 when medianWaitMinutes is null', async () => {
    vi.spyOn(apiClient, 'get').mockResolvedValueOnce({
      data: {
        ...SAMPLE_REPORT_DTO,
        summary: {
          ...SAMPLE_REPORT_DTO.summary,
          averageWaitMinutes: null,
          medianWaitMinutes: null,
        },
      },
    } as AxiosResponse);

    renderPage();

    expect(await screen.findByText('Chưa đủ dữ liệu hàng đợi')).toBeInTheDocument();
    expect(screen.getAllByText('—').length).toBeGreaterThan(0);
  });

  it('filters by department via API query param and supports search + Ctrl+K', async () => {
    const getSpy = vi.spyOn(apiClient, 'get');
    renderPage();

    await screen.findByText('RPT-DEP-GI');

    const searchInput = screen.getByLabelText('Tìm tên báo cáo, mã báo cáo hoặc kỳ tổng hợp');
    fireEvent.keyDown(window, { key: 'k', ctrlKey: true });
    expect(document.activeElement).toBe(searchInput);

    fireEvent.change(searchInput, { target: { value: 'RPT-DEP-CAR' } });
    expect(screen.getByText('RPT-DEP-CAR')).toBeInTheDocument();
    expect(screen.queryByText('RPT-DEP-GI')).not.toBeInTheDocument();

    fireEvent.change(screen.getByLabelText('Lọc khoa phòng'), { target: { value: '10' } });
    await waitFor(() => {
      expect(getSpy).toHaveBeenCalledWith(
        '/admin/reports/operational',
        expect.objectContaining({
          params: expect.objectContaining({ departmentId: 10 }),
        }),
      );
    });
  });
});
