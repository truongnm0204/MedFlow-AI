import { Bot, KeyRound, Mail, QrCode, Settings2 } from 'lucide-react';

import { Badge } from '@/shared/ui/badge';

import type {
  IntegrationProviderItemDto,
  IntegrationProviderKey,
  IntegrationStatus,
} from '../types/integration-config.types';

interface IntegrationProviderCardsProps {
  integrations: IntegrationProviderItemDto[];
  selectedProviderKey: IntegrationProviderKey;
  onSelectProvider: (key: IntegrationProviderKey) => void;
}

const STATUS_META: Record<string, { label: string; tone: 'emerald' | 'slate' | 'amber' }> = {
  ACTIVE: { label: 'Đang hoạt động', tone: 'emerald' },
  DISABLED: { label: 'Đang tạm tắt', tone: 'slate' },
  NEEDS_SETUP: { label: 'Cần bổ sung cấu hình', tone: 'amber' },
};

function resolveStatusMeta(status: IntegrationStatus) {
  return STATUS_META[status] ?? { label: status, tone: 'slate' as const };
}

function getProviderIcon(key: IntegrationProviderKey) {
  if (key === 'VIETQR_SEPAY') return QrCode;
  if (key === 'SMTP_EMAIL') return Mail;
  return Bot;
}

function renderSummaryLine(item: IntegrationProviderItemDto): string {
  if (item.vietQr) {
    const bank = item.vietQr.bankBin
      ? `BIN ${item.vietQr.bankBin} • STK ${item.vietQr.accountNumber ?? '---'}`
      : 'Chưa khai báo tài khoản nhận QR';
    return `${bank} • Hết hạn ${item.vietQr.pendingExpiryMinutes} phút`;
  }
  if (item.smtp) {
    return `${item.smtp.host}:${item.smtp.port} • ${item.smtp.fromDisplayName} (${item.smtp.timeoutSeconds}s)`;
  }
  if (item.ai) {
    return `${item.ai.providerName} • ${item.ai.modelName} • Tối đa ${item.ai.maxSuggestions} gợi ý`;
  }
  return item.category;
}

export function IntegrationProviderCards({
  integrations,
  selectedProviderKey,
  onSelectProvider,
}: IntegrationProviderCardsProps) {
  return (
    <section
      aria-label="Danh sách các tích hợp dịch vụ bên ngoài"
      className="grid grid-cols-1 gap-4 lg:grid-cols-3"
    >
      {integrations.map((item) => {
        const Icon = getProviderIcon(item.providerKey);
        const statusMeta = resolveStatusMeta(item.status);
        const isSelected = selectedProviderKey === item.providerKey;

        return (
          <div
            key={item.providerKey}
            role="button"
            tabIndex={0}
            aria-pressed={isSelected}
            onClick={() => onSelectProvider(item.providerKey)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                onSelectProvider(item.providerKey);
              }
            }}
            className={`flex cursor-pointer flex-col justify-between rounded-2xl border p-5 text-left transition-all focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none ${
              isSelected
                ? 'border-primary bg-sky-50/40 shadow-sm'
                : 'border-border bg-white hover:border-sky-300'
            }`}
          >
            <div className="space-y-3">
              <div className="flex items-start justify-between gap-2">
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl border border-sky-200 bg-sky-50 text-primary">
                  <Icon className="h-5 w-5" aria-hidden="true" />
                </div>

                <div className="flex flex-wrap items-center justify-end gap-1.5">
                  <Badge tone={statusMeta.tone}>{statusMeta.label}</Badge>
                  <Badge tone="slate">
                    {item.policyVersion > 0 ? `v${item.policyVersion}` : 'Mặc định'}
                  </Badge>
                </div>
              </div>

              <div>
                <div className="font-mono text-[10px] font-bold tracking-wider uppercase text-primary">
                  {item.providerKey}
                </div>
                <h2 className="mt-0.5 text-sm font-bold text-ink">{item.displayName}</h2>
                <p className="mt-1 text-xs leading-relaxed text-sub">{item.description}</p>
              </div>

              <div className="rounded-xl border border-border bg-canvas p-2.5 text-[11px]">
                <div className="font-mono font-semibold text-ink">{renderSummaryLine(item)}</div>
                <div className="mt-1 flex items-center gap-1.5 text-sub">
                  <KeyRound className="h-3.5 w-3.5 shrink-0 text-primary" aria-hidden="true" />
                  <span>
                    {item.secretStatus.secretName}:{' '}
                    {item.secretStatus.isConfigured ? (
                      <strong className="font-mono text-emerald-700">
                        {item.secretStatus.maskedValue ?? '••••••••••••'} (Đã cấu hình)
                      </strong>
                    ) : (
                      <span className="font-semibold text-amber-700">Chưa nạp khóa hạ tầng</span>
                    )}
                  </span>
                </div>
              </div>
            </div>

            <div className="mt-4 flex items-center justify-between border-t border-border pt-3 text-xs">
              <span className="text-sub">
                {item.enabled ? 'Chế độ: Đang bật' : 'Chế độ: Tạm tắt'}
              </span>
              <span className="inline-flex items-center gap-1 font-semibold text-primary">
                <Settings2 className="h-3.5 w-3.5" aria-hidden="true" />
                <span>{isSelected ? 'Đang thiết lập' : 'Chọn cấu hình'}</span>
              </span>
            </div>
          </div>
        );
      })}
    </section>
  );
}
