import { Check, Copy, FileCode2, ShieldCheck, X } from 'lucide-react';
import { useEffect, useState } from 'react';
import { createPortal } from 'react-dom';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Spinner } from '@/shared/ui/spinner';

import { useAuditLogDetail } from '../hooks/use-audit-logs';
import {
  formatHospitalDateTimeDisplay,
  formatPrettyMetadataJson,
} from '../schemas/audit-log.schema';
import type { AuditLogListItemDto } from '../types/audit-log.types';

interface AuditLogDetailDrawerProps {
  selectedItem: AuditLogListItemDto | null;
  onClose: () => void;
}

export function AuditLogDetailDrawer({ selectedItem, onClose }: AuditLogDetailDrawerProps) {
  const [hasCopied, setHasCopied] = useState(false);
  const detailQuery = useAuditLogDetail(selectedItem?.id ?? null);

  useEffect(() => {
    if (!selectedItem) return undefined;
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose();
      }
    };
    document.addEventListener('keydown', handleKeyDown);
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [selectedItem, onClose]);

  if (!selectedItem) {
    return null;
  }

  const detail = detailQuery.data;
  const effectiveChangeData = detail?.changeData ?? selectedItem.changeData;
  const prettyJson = formatPrettyMetadataJson(effectiveChangeData);
  const redactedFields = detail?.redactedFields ?? [];

  const handleCopyJson = async () => {
    if (!prettyJson) return;
    try {
      await navigator.clipboard?.writeText(prettyJson);
      setHasCopied(true);
      window.setTimeout(() => setHasCopied(false), 2000);
    } catch {
      // Clipboard API may be restricted in non-HTTPS contexts
    }
  };

  return createPortal(
    <div
      className="fixed inset-0 z-50 flex justify-end bg-navy/45 backdrop-blur-xs"
      onClick={onClose}
      role="dialog"
      aria-modal="true"
      aria-labelledby="audit-drawer-title"
    >
      <div
        className="flex h-full w-full max-w-2xl flex-col border-l border-border bg-white shadow-2xl"
        onClick={(e) => e.stopPropagation()}
      >
        {/* Drawer Header */}
        <div className="flex items-start justify-between gap-4 border-b border-border px-6 py-4">
          <div className="space-y-1">
            <div className="flex flex-wrap items-center gap-2">
              <span className="font-mono text-xs font-bold text-primary">
                {detail?.auditCode ?? selectedItem.auditCode}
              </span>
              <Badge tone="sky">{detail?.category ?? selectedItem.category}</Badge>
            </div>
            <h2 id="audit-drawer-title" className="font-mono text-base font-bold text-ink">
              {detail?.action ?? selectedItem.action}
            </h2>
          </div>

          <button
            type="button"
            onClick={onClose}
            aria-label="Đóng ngăn chi tiết nhật ký"
            className="rounded-lg p-1.5 text-sub transition-colors hover:bg-canvas hover:text-ink focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
          >
            <X className="h-5 w-5" aria-hidden="true" />
          </button>
        </div>

        {/* Drawer Body */}
        <div className="flex-1 space-y-5 overflow-y-auto p-6">
          {detailQuery.isPending && (
            <div className="flex items-center gap-2 text-xs text-sub">
              <Spinner className="h-4 w-4 text-primary" />
              <span>Đang đồng bộ chi tiết bản ghi kiểm toán...</span>
            </div>
          )}

          {detailQuery.isError && (
            <Alert tone="error">
              {getErrorMessage(
                detailQuery.error,
                'Không thể tải chi tiết mở rộng của nhật ký kiểm toán.',
              )}
            </Alert>
          )}

          <dl className="grid grid-cols-1 gap-3 rounded-xl border border-border bg-canvas p-4 text-xs sm:grid-cols-2">
            <div>
              <dt className="font-semibold text-sub">Đối tượng tác động</dt>
              <dd className="mt-0.5 font-mono font-bold text-ink">
                {detail?.entityType ?? selectedItem.entityType}
                {(detail?.entityId ?? selectedItem.entityId)
                  ? ` • #${detail?.entityId ?? selectedItem.entityId}`
                  : ''}
              </dd>
            </div>

            <div>
              <dt className="font-semibold text-sub">Phạm vi cơ sở y tế</dt>
              <dd className="mt-0.5 font-semibold text-ink">
                {detail?.hospitalName ??
                  selectedItem.hospitalName ??
                  (selectedItem.hospitalId ? `Cơ sở #${selectedItem.hospitalId}` : 'Toàn hệ thống')}
              </dd>
            </div>

            <div>
              <dt className="font-semibold text-sub">Tác nhân thực hiện</dt>
              <dd className="mt-0.5 text-ink">
                <span className="font-semibold">
                  {detail?.actorFullName ??
                    selectedItem.actorFullName ??
                    detail?.actorEmail ??
                    selectedItem.actorEmail ??
                    'Hệ thống'}
                </span>
                {(detail?.actorUserId ?? selectedItem.actorUserId) ? (
                  <span className="ml-1 font-mono text-sub">
                    (UID #{detail?.actorUserId ?? selectedItem.actorUserId})
                  </span>
                ) : null}
              </dd>
            </div>

            <div>
              <dt className="font-semibold text-sub">Địa chỉ IP nguồn</dt>
              <dd className="mt-0.5 font-mono text-ink">
                {detail?.ipAddress ?? selectedItem.ipAddress ?? 'N/A'}
              </dd>
            </div>

            <div>
              <dt className="font-semibold text-sub">Thời điểm bệnh viện (UTC+7)</dt>
              <dd className="mt-0.5 font-mono text-ink">
                {formatHospitalDateTimeDisplay(
                  detail?.occurredAtHospitalLocal ?? selectedItem.occurredAtHospitalLocal,
                  detail?.occurredAt ?? selectedItem.occurredAt,
                )}
              </dd>
            </div>

            <div>
              <dt className="font-semibold text-sub">Thời điểm chuẩn UTC</dt>
              <dd className="mt-0.5 font-mono text-sub">
                {detail?.occurredAt ?? selectedItem.occurredAt}
              </dd>
            </div>

            <div className="sm:col-span-2">
              <dt className="font-semibold text-sub">Lý do / Ghi chú nghiệp vụ</dt>
              <dd className="mt-0.5 text-ink">
                {detail?.reason ?? selectedItem.reason ?? 'Không có ghi chú bổ sung.'}
              </dd>
            </div>
          </dl>

          {redactedFields.length > 0 && (
            <Alert tone="info">
              <div className="flex items-center gap-1.5 font-semibold">
                <ShieldCheck className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
                <span>Bảo vệ dữ liệu nhạy cảm</span>
              </div>
              <p className="mt-0.5">
                Hệ thống đã tự động che ({'***REDACTED***'}) các trường nhạy cảm:{' '}
                <code className="font-mono font-semibold">{redactedFields.join(', ')}</code>.
              </p>
            </Alert>
          )}

          {/* JSON Metadata Section */}
          <div className="space-y-2">
            <div className="flex items-center justify-between gap-2">
              <div className="flex items-center gap-1.5 text-xs font-bold text-ink">
                <FileCode2 className="h-4 w-4 text-primary" aria-hidden="true" />
                <span>Dữ liệu thay đổi (JSON Metadata)</span>
              </div>
              {prettyJson ? (
                <Button type="button" variant="secondary" size="sm" onClick={handleCopyJson}>
                  {hasCopied ? (
                    <>
                      <Check className="h-3.5 w-3.5 text-emerald-600" aria-hidden="true" />
                      <span>Đã sao chép</span>
                    </>
                  ) : (
                    <>
                      <Copy className="h-3.5 w-3.5" aria-hidden="true" />
                      <span>Sao chép JSON</span>
                    </>
                  )}
                </Button>
              ) : null}
            </div>

            {prettyJson ? (
              <pre
                data-testid="audit-metadata-json"
                className="max-h-96 overflow-auto rounded-xl border border-border bg-navy p-4 font-mono text-xs leading-relaxed text-sky-100"
              >
                {prettyJson}
              </pre>
            ) : (
              <div className="rounded-xl border border-dashed border-border bg-canvas p-6 text-center text-xs text-sub">
                Sự kiện này không ghi nhận payload JSON chi tiết trong <code>change_data</code>.
              </div>
            )}
          </div>
        </div>

        {/* Drawer Footer */}
        <div className="flex justify-end border-t border-border bg-canvas px-6 py-3">
          <Button type="button" variant="secondary" size="sm" onClick={onClose}>
            Đóng ngăn chi tiết
          </Button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
