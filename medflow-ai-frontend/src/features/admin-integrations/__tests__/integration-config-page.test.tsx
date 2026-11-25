import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router-dom';
import { beforeEach, describe, expect, it, vi } from 'vitest';

import { RequireAdmin, RequireAuth, RequireSystemAdmin } from '@/app/router/guards';
import { useAuthStore } from '@/features/auth/store/auth.store';

import * as integrationApi from '../api/integration-config.api';
import { IntegrationConfigPage } from '../pages/integration-config-page';
import type { IntegrationConfigListResponseDto } from '../types/integration-config.types';

const MOCK_INTEGRATIONS_RESPONSE: IntegrationConfigListResponseDto = {
  hospitalId: 1,
  hospitalCode: 'HOS-01',
  hospitalName: 'Bệnh viện MedFlow Cơ sở 1',
  canEdit: true,
  generatedAtUtc: '2026-11-25T02:00:00Z',
  availableHospitals: [
    { id: 1, code: 'HOS-01', name: 'Bệnh viện MedFlow Cơ sở 1' },
    { id: 2, code: 'HOS-02', name: 'Bệnh viện MedFlow Cơ sở 2' },
  ],
  integrations: [
    {
      providerKey: 'VIETQR_SEPAY',
      displayName: 'VietQR & Cổng đối soát SePay',
      category: 'PAYMENT',
      description: 'Sinh mã VietQR theo viện phí lượt khám.',
      enabled: true,
      status: 'ACTIVE',
      policyVersion: 2,
      lastUpdatedAt: '2026-11-24T10:00:00Z',
      secretStatus: {
        secretName: 'SePay:WebhookApiKey',
        isConfigured: true,
        maskedValue: '••••••••••••',
        sourceHint: 'Biến môi trường / User Secrets (chỉ đọc)',
      },
      vietQr: {
        bankBin: '970422',
        accountNumber: '0123456789',
        accountName: 'BENH VIEN MEDFLOW HA NOI',
        transferPrefix: 'MFHN',
        pendingExpiryMinutes: 15,
        webhookEndpointPath: '/api/v1/payments/sepay/webhook',
      },
      smtp: null,
      ai: null,
    },
    {
      providerKey: 'SMTP_EMAIL',
      displayName: 'Dịch vụ Email Giao dịch (SMTP)',
      category: 'NOTIFICATION',
      description: 'Gửi mã OTP và thông báo lịch hẹn.',
      enabled: true,
      status: 'ACTIVE',
      policyVersion: 1,
      lastUpdatedAt: '2026-11-24T09:00:00Z',
      secretStatus: {
        secretName: 'Smtp:Password',
        isConfigured: true,
        maskedValue: '••••••••••••',
        sourceHint: 'Biến môi trường / User Secrets (chỉ đọc)',
      },
      vietQr: null,
      smtp: {
        host: 'smtp.gmail.com',
        port: 587,
        senderIdentity: 'no***@medflow.vn',
        fromDisplayName: 'MedFlow AI',
        replyToEmail: 'cskh@medflow.vn',
        subjectPrefix: '[MedFlow]',
        timeoutSeconds: 30,
      },
      ai: null,
    },
    {
      providerKey: 'AI_PROVIDER',
      displayName: 'Trợ lý AI Điều hướng Chuyên khoa & Lịch khám',
      category: 'AI_ASSISTANT',
      description: 'Phân tích triệu chứng gợi ý chuyên khoa.',
      enabled: true,
      status: 'NEEDS_SETUP',
      policyVersion: 0,
      lastUpdatedAt: null,
      secretStatus: {
        secretName: 'AiRecommendation:ApiKey',
        isConfigured: false,
        maskedValue: null,
        sourceHint: 'Biến môi trường / User Secrets (chỉ đọc)',
      },
      vietQr: null,
      smtp: null,
      ai: {
        providerName: 'GEMINI',
        modelName: 'gemini-2.5-flash',
        timeoutSeconds: 15,
        maxSuggestions: 3,
        disclaimerTemplate:
          'Gợi ý từ AI chỉ mang tính tham khảo điều hướng khám, không thay thế chẩn đoán y khoa của bác sĩ.',
      },
    },
  ],
};

function LocationProbe() {
  const loc = useLocation();
  return <div data-testid="location-probe">{loc.pathname + loc.search}</div>;
}

function renderIntegrationPage() {
  const qc = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={qc}>
      <MemoryRouter initialEntries={['/admin/integrations']}>
        <IntegrationConfigPage />
      </MemoryRouter>
    </QueryClientProvider>,
  );
}

describe('IntegrationConfigPage (UC59 — #241, #243)', () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    localStorage.clear();
    vi.spyOn(integrationApi, 'fetchIntegrationConfigs').mockResolvedValue(
      MOCK_INTEGRATIONS_RESPONSE,
    );

    useAuthStore.setState({
      user: {
        id: 13,
        email: 'system_admin01@medflow.test',
        fullName: 'QTHT Lưu Huy',
        roles: ['SYSTEM_ADMIN'],
        patientCode: null,
        staffCode: 'STF-011',
      },
      accessToken: 'tok-sysadmin',
      refreshToken: 'rtok-sysadmin',
      isAuthenticated: true,
    });
  });

  it('hien thi 3 the tich hop, che khoa bi mat runtime va xac thuc du lieu truoc khi luu', async () => {
    const updateSpy = vi.spyOn(integrationApi, 'updateIntegrationConfig').mockResolvedValue({
      ...MOCK_INTEGRATIONS_RESPONSE.integrations[0],
      policyVersion: 3,
    });

    renderIntegrationPage();

    await waitFor(() => {
      expect(screen.getByText('VietQR & Cổng đối soát SePay')).toBeInTheDocument();
    });

    expect(screen.getByText('Dịch vụ Email Giao dịch (SMTP)')).toBeInTheDocument();
    expect(screen.getByText('Trợ lý AI Điều hướng Chuyên khoa & Lịch khám')).toBeInTheDocument();
    expect(screen.getAllByText(/••••••••••••/).length).toBeGreaterThan(0);

    // Invalid BIN validation blocks API call
    const binInput = screen.getByLabelText(/Mã BIN ngân hàng thụ hưởng/i);
    fireEvent.change(binInput, { target: { value: '99' } });
    fireEvent.click(screen.getByRole('button', { name: /Lưu cấu hình tích hợp/i }));

    expect(
      screen.getAllByText('Mã BIN ngân hàng phải gồm đúng 6 chữ số (VD: 970422).').length,
    ).toBeGreaterThan(0);
    expect(updateSpy).not.toHaveBeenCalled();

    // Valid VietQR update calls API
    fireEvent.change(binInput, { target: { value: '970436' } });
    fireEvent.change(screen.getByLabelText(/Ghi chú kiểm toán thay đổi/i), {
      target: { value: 'Chuyển sang tài khoản Vietcombank' },
    });
    fireEvent.click(screen.getByRole('button', { name: /Lưu cấu hình tích hợp/i }));

    await waitFor(() => {
      expect(updateSpy).toHaveBeenCalledWith('VIETQR_SEPAY', {
        hospitalId: 1,
        enabled: true,
        bankBin: '970436',
        accountNumber: '0123456789',
        accountName: 'BENH VIEN MEDFLOW HA NOI',
        transferPrefix: 'MFHN',
        pendingExpiryMinutes: 15,
        reason: 'Chuyển sang tài khoản Vietcombank',
      });
    });

    expect(
      await screen.findByText(/Đã lưu cấu hình VietQR & Cổng đối soát SePay \(phiên bản v3\)/),
    ).toBeInTheDocument();
  });

  it('cho phep chuyen sang tab AI Provider / SMTP va an nut luu khi canEdit = false', async () => {
    vi.spyOn(integrationApi, 'fetchIntegrationConfigs').mockResolvedValueOnce({
      ...MOCK_INTEGRATIONS_RESPONSE,
      canEdit: false,
    });

    const { unmount } = renderIntegrationPage();

    await waitFor(() => {
      expect(screen.getByText('VietQR & Cổng đối soát SePay')).toBeInTheDocument();
    });

    // Save button hidden when unauthorized to edit
    expect(
      screen.queryByRole('button', { name: /Lưu cấu hình tích hợp/i }),
    ).not.toBeInTheDocument();
    expect(
      screen.getByText(/Tài khoản hiện tại chỉ có quyền xem trạng thái tích hợp/i),
    ).toBeInTheDocument();

    unmount();

    // Switch to AI Provider when editable
    renderIntegrationPage();
    await waitFor(() => {
      expect(screen.getByText('Trợ lý AI Điều hướng Chuyên khoa & Lịch khám')).toBeInTheDocument();
    });

    fireEvent.click(screen.getByText('Trợ lý AI Điều hướng Chuyên khoa & Lịch khám'));
    expect(screen.getByLabelText('Chế độ / Nhà cung cấp AI')).toBeInTheDocument();
    expect(screen.getByLabelText('Mẫu tuyên bố miễn trừ trách nhiệm y khoa')).toBeInTheDocument();
  });

  it('bao ve route /admin/integrations va /system-admin/integrations', () => {
    useAuthStore.setState({
      user: {
        id: 1,
        email: 'patient01@medflow.test',
        fullName: 'Nguyễn Văn An',
        roles: ['PATIENT'],
        patientCode: 'PAT-0001',
      },
      accessToken: 'tok-patient',
      refreshToken: 'rtok-patient',
      isAuthenticated: true,
    });

    const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
    render(
      <QueryClientProvider client={qc}>
        <MemoryRouter initialEntries={['/admin/integrations']}>
          <Routes>
            <Route
              path="/admin/integrations"
              element={
                <RequireAuth>
                  <RequireAdmin>
                    <IntegrationConfigPage />
                  </RequireAdmin>
                </RequireAuth>
              }
            />
            <Route
              path="/system-admin/integrations"
              element={
                <RequireAuth>
                  <RequireSystemAdmin>
                    <IntegrationConfigPage />
                  </RequireSystemAdmin>
                </RequireAuth>
              }
            />
            <Route path="/" element={<div>Patient Home</div>} />
          </Routes>
          <LocationProbe />
        </MemoryRouter>
      </QueryClientProvider>,
    );

    expect(screen.getByTestId('location-probe')).toHaveTextContent('/');
  });
});
