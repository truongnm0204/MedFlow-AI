import { FileSearch, Plug, RefreshCw, ShieldCheck } from 'lucide-react';
import { useState } from 'react';
import { Link } from 'react-router-dom';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';
import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert, Badge, Button, Spinner, toast } from '@/shared/ui';

import { IntegrationConfigForm } from '../components/integration-config-form';
import { IntegrationProviderCards } from '../components/integration-provider-cards';
import { useIntegrationConfigs, useUpdateIntegrationConfig } from '../hooks/use-integrations';
import type {
  IntegrationProviderKey,
  UpdateIntegrationConfigPayload,
} from '../types/integration-config.types';

export function IntegrationConfigPage() {
  const user = useAuthStore((s) => s.user);
  const isSystemAdmin = Boolean(user?.roles.includes('SYSTEM_ADMIN'));
  const hasAdminRole = Boolean(
    user?.roles.includes('SYSTEM_ADMIN') || user?.roles.includes('HOSPITAL_ADMIN'),
  );

  const [selectedHospitalId, setSelectedHospitalId] = useState<number | null>(null);
  const [selectedProviderKey, setSelectedProviderKey] =
    useState<IntegrationProviderKey>('VIETQR_SEPAY');
  const [statusBanner, setStatusBanner] = useState<string | null>(null);
  const [saveError, setSaveError] = useState<string | null>(null);

  const configsQuery = useIntegrationConfigs(selectedHospitalId, true);
  const updateMutation = useUpdateIntegrationConfig();

  const integrations = configsQuery.data?.integrations ?? [];
  const activeProvider =
    integrations.find((i) => i.providerKey === selectedProviderKey) ?? integrations[0] ?? null;

  const canEdit = Boolean(configsQuery.data?.canEdit && hasAdminRole);

  const handleSaveProvider = async (payload: UpdateIntegrationConfigPayload) => {
    if (!activeProvider) return;
    setStatusBanner(null);
    setSaveError(null);
    try {
      const updated = await updateMutation.mutateAsync({
        providerKey: activeProvider.providerKey,
        payload,
      });
      const msg = `Đã lưu cấu hình ${updated.displayName} (phiên bản v${updated.policyVersion}).`;
      setStatusBanner(msg);
      toast.success(msg, 'Cập nhật tích hợp thành công');
    } catch (err) {
      const msg = getErrorMessage(err, 'Không thể lưu cấu hình tích hợp. Vui lòng thử lại.');
      setSaveError(msg);
      toast.error(msg, 'Lỗi lưu cấu hình');
    }
  };

  return (
    <AdminPortalShell
      activeTab="Cấu hình tích hợp"
      breadcrumbs={[
        {
          label: isSystemAdmin ? 'Quản trị Hệ thống' : 'Cấu hình Cơ sở',
          href: isSystemAdmin ? '/system-admin' : '/hospital-admin',
        },
        { label: 'Cấu hình tích hợp (UC59)' },
      ]}
    >
      <main className="mx-auto max-w-[1680px] space-y-6 p-6">
        {/* Page Header */}
        <div className="flex flex-col justify-between gap-4 rounded-2xl border border-border bg-white p-5 shadow-sm lg:flex-row lg:items-center">
          <div className="space-y-1">
            <div className="flex flex-wrap items-center gap-2">
              <Badge tone="sky">
                <Plug className="mr-1 h-3.5 w-3.5" aria-hidden="true" />
                Quản lý tích hợp dịch vụ ngoài (UC59)
              </Badge>
              {configsQuery.data && (
                <Badge tone="emerald">
                  {configsQuery.data.hospitalName} ({configsQuery.data.hospitalCode})
                </Badge>
              )}
              <span className="font-mono text-[11px] text-sub">Mã màn hình: SCR-ADM-INT-01</span>
            </div>
            <h1 className="text-xl font-bold tracking-tight text-ink sm:text-2xl">
              Cấu hình Tích hợp Hệ thống (Integration Configuration)
            </h1>
            <p className="text-xs text-sub">
              Quản lý trạng thái kết nối và thông số vận hành cho cổng thanh toán VietQR/SePay, dịch
              vụ Email SMTP và Trợ lý AI theo cơ sở. Khóa bí mật luôn được che bảo mật.
            </p>
          </div>

          <div className="flex flex-wrap items-center gap-2.5">
            {isSystemAdmin && (configsQuery.data?.availableHospitals.length ?? 0) > 0 && (
              <div className="flex items-center gap-2">
                <label
                  htmlFor="integration-hospital-select"
                  className="text-xs font-semibold text-sub"
                >
                  Cơ sở:
                </label>
                <select
                  id="integration-hospital-select"
                  aria-label="Chọn cơ sở y tế cấu hình tích hợp"
                  value={String(selectedHospitalId ?? configsQuery.data?.hospitalId ?? '')}
                  onChange={(e) => {
                    setStatusBanner(null);
                    setSaveError(null);
                    setSelectedHospitalId(e.target.value ? Number(e.target.value) : null);
                  }}
                  className="h-9 rounded-lg border border-border bg-canvas px-3 text-xs font-semibold text-ink focus:border-primary focus:bg-white focus:outline-none"
                >
                  {configsQuery.data?.availableHospitals.map((h) => (
                    <option key={h.id} value={String(h.id)}>
                      {h.name} ({h.code})
                    </option>
                  ))}
                </select>
              </div>
            )}

            <Link
              to={isSystemAdmin ? '/system-admin/audit-logs' : '/admin/audit-logs'}
              className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-white px-3 py-1.5 text-xs font-semibold text-ink transition-colors hover:bg-canvas"
            >
              <FileSearch className="h-3.5 w-3.5 text-primary" aria-hidden="true" />
              <span>Xem nhật ký kiểm toán</span>
            </Link>

            <Button
              type="button"
              variant="secondary"
              size="sm"
              onClick={() => void configsQuery.refetch()}
              disabled={configsQuery.isFetching}
            >
              <RefreshCw
                className={`h-3.5 w-3.5 ${configsQuery.isFetching ? 'animate-spin' : ''}`}
                aria-hidden="true"
              />
              <span>Làm mới</span>
            </Button>
          </div>
        </div>

        {/* Security Notice Banner */}
        <Alert tone="info">
          <div className="flex items-center gap-1.5 font-semibold">
            <ShieldCheck className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
            <span>Nguyên tắc an toàn khóa tích hợp:</span>
          </div>
          <p className="mt-0.5">
            Các khóa bí mật xác thực (<code className="font-mono">WebhookApiKey</code>,{' '}
            <code className="font-mono">Smtp:Password</code>,{' '}
            <code className="font-mono">AiRecommendation:ApiKey</code>) được quản lý qua biến môi
            trường hạ tầng và không bao giờ trả khóa thô về trình duyệt.
          </p>
        </Alert>

        {statusBanner && (
          <div role="status">
            <Alert tone="success">{statusBanner}</Alert>
          </div>
        )}

        {saveError && (
          <div role="alert">
            <Alert tone="error">{saveError}</Alert>
          </div>
        )}

        {configsQuery.isPending ? (
          <div
            role="status"
            className="flex flex-col items-center justify-center gap-3 rounded-2xl border border-border bg-white py-16 text-xs text-sub shadow-sm"
          >
            <Spinner className="h-6 w-6 text-primary" />
            <span className="font-medium">Đang tải danh sách cấu hình tích hợp...</span>
          </div>
        ) : configsQuery.isError ? (
          <div role="alert">
            <Alert tone="error">
              <div className="flex flex-wrap items-center justify-between gap-3">
                <div>
                  <div className="font-bold">Không thể tải cấu hình tích hợp</div>
                  <div className="mt-0.5">
                    {getErrorMessage(
                      configsQuery.error,
                      'Không thể kết nối máy chủ hoặc tài khoản chưa được cấp quyền HOSPITAL_CONFIG.',
                    )}
                  </div>
                </div>
                <Button
                  type="button"
                  variant="secondary"
                  size="sm"
                  onClick={() => void configsQuery.refetch()}
                >
                  Thử lại
                </Button>
              </div>
            </Alert>
          </div>
        ) : integrations.length === 0 ? (
          <div className="rounded-2xl border border-dashed border-border bg-white p-12 text-center text-xs text-sub">
            Chưa có danh mục nhà cung cấp tích hợp nào khả dụng cho cơ sở hiện tại.
          </div>
        ) : (
          <>
            <IntegrationProviderCards
              integrations={integrations}
              selectedProviderKey={activeProvider?.providerKey ?? selectedProviderKey}
              onSelectProvider={(key) => {
                setStatusBanner(null);
                setSaveError(null);
                setSelectedProviderKey(key);
              }}
            />

            {activeProvider && configsQuery.data && (
              <IntegrationConfigForm
                key={`${configsQuery.data.hospitalId}-${activeProvider.providerKey}-${activeProvider.policyVersion}`}
                provider={activeProvider}
                hospitalId={configsQuery.data.hospitalId}
                hospitalName={configsQuery.data.hospitalName}
                canEdit={canEdit}
                isSaving={updateMutation.isPending}
                onSave={handleSaveProvider}
              />
            )}
          </>
        )}
      </main>
    </AdminPortalShell>
  );
}
