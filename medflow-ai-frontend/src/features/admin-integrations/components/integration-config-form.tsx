import { KeyRound, Lock, Save, Undo2 } from 'lucide-react';
import { type FormEvent, useRef, useState } from 'react';

import { Alert } from '@/shared/ui/alert';
import { Badge } from '@/shared/ui/badge';
import { Button } from '@/shared/ui/button';
import { Card, CardContent, CardFooter, CardHeader } from '@/shared/ui/card';

import {
  buildInitialFormState,
  type IntegrationFormFieldErrors,
  type IntegrationFormState,
  validateIntegrationForm,
} from '../schemas/integration-config.schema';
import type {
  IntegrationProviderItemDto,
  UpdateIntegrationConfigPayload,
} from '../types/integration-config.types';

interface IntegrationConfigFormProps {
  provider: IntegrationProviderItemDto;
  hospitalId: number;
  hospitalName: string;
  canEdit: boolean;
  isSaving: boolean;
  onSave: (payload: UpdateIntegrationConfigPayload) => Promise<void>;
}

export function IntegrationConfigForm({
  provider,
  hospitalId,
  hospitalName,
  canEdit,
  isSaving,
  onSave,
}: IntegrationConfigFormProps) {
  const [formState, setFormState] = useState<IntegrationFormState>(() =>
    buildInitialFormState(provider),
  );
  const [fieldErrors, setFieldErrors] = useState<IntegrationFormFieldErrors>({});
  const errorSummaryRef = useRef<HTMLDivElement | null>(null);

  const updateField = <K extends keyof IntegrationFormState>(
    field: K,
    value: IntegrationFormState[K],
  ) => {
    setFormState((prev) => ({ ...prev, [field]: value }));
    if (fieldErrors[field]) {
      setFieldErrors((prev) => ({ ...prev, [field]: undefined }));
    }
  };

  const handleReset = () => {
    setFormState(buildInitialFormState(provider));
    setFieldErrors({});
  };

  const handleSubmit = async (e: FormEvent<HTMLFormElement>) => {
    e.preventDefault();
    if (!canEdit) return;

    const validation = validateIntegrationForm(provider.providerKey, formState, hospitalId);
    if (!validation.isValid || !validation.payload) {
      setFieldErrors(validation.errors);
      window.requestAnimationFrame(() => {
        errorSummaryRef.current?.focus();
      });
      return;
    }

    setFieldErrors({});
    await onSave(validation.payload);
  };

  const errorEntries = Object.entries(fieldErrors).filter((entry): entry is [string, string] =>
    Boolean(entry[1]),
  );

  return (
    <Card>
      <form onSubmit={(e) => void handleSubmit(e)} noValidate>
        <CardHeader className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <div className="flex flex-wrap items-center gap-2">
              <span className="font-mono text-xs font-bold text-primary">
                {provider.providerKey}
              </span>
              <Badge tone="sky">{hospitalName}</Badge>
            </div>
            <h2 className="mt-1 text-base font-bold text-ink">
              Thiết lập cấu hình: {provider.displayName}
            </h2>
          </div>

          <label className="inline-flex cursor-pointer items-center gap-2.5 rounded-xl border border-border bg-canvas px-3.5 py-2 text-xs font-semibold text-ink">
            <input
              type="checkbox"
              checked={formState.enabled}
              disabled={!canEdit || isSaving}
              onChange={(e) => updateField('enabled', e.target.checked)}
              className="h-4 w-4 rounded border-border text-primary focus:ring-primary"
            />
            <span>{formState.enabled ? 'Đang bật tích hợp' : 'Đang tạm tắt tích hợp'}</span>
          </label>
        </CardHeader>

        <CardContent className="space-y-5">
          {errorEntries.length > 0 && (
            <div
              ref={errorSummaryRef}
              tabIndex={-1}
              role="alert"
              aria-labelledby="integration-error-summary-title"
              className="rounded-xl border border-rose-200 bg-rose-50 p-4 text-xs text-rose-800 focus:outline-none"
            >
              <p id="integration-error-summary-title" className="font-bold">
                Vui lòng kiểm tra lại thông tin cấu hình:
              </p>
              <ul className="mt-1.5 list-disc space-y-1 pl-4">
                {errorEntries.map(([key, msg]) => (
                  <li key={key}>{msg}</li>
                ))}
              </ul>
            </div>
          )}

          {/* Read-only Runtime Secret Box */}
          <div className="rounded-xl border border-border bg-canvas p-4 text-xs">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="flex items-center gap-2 font-bold text-ink">
                <Lock className="h-4 w-4 text-primary" aria-hidden="true" />
                <span>Khóa bí mật hạ tầng ({provider.secretStatus.secretName})</span>
              </div>
              <Badge tone={provider.secretStatus.isConfigured ? 'emerald' : 'amber'}>
                {provider.secretStatus.isConfigured ? 'Đã nạp bảo mật' : 'Chưa cấu hình'}
              </Badge>
            </div>
            <div className="mt-2 flex flex-wrap items-center gap-2 text-sub">
              <KeyRound className="h-3.5 w-3.5 text-sub" aria-hidden="true" />
              <span>Giá trị hiển thị an toàn:</span>
              <code className="rounded border border-border bg-white px-2 py-0.5 font-mono text-xs font-semibold text-ink">
                {provider.secretStatus.maskedValue ?? 'Chưa thiết lập'}
              </code>
              <span>• {provider.secretStatus.sourceHint}</span>
            </div>
          </div>

          {/* Provider-Specific Fields */}
          {provider.providerKey === 'VIETQR_SEPAY' && (
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              <div className="space-y-1.5">
                <label htmlFor="vietqr-bank-bin" className="block text-xs font-semibold text-ink">
                  Mã BIN ngân hàng thụ hưởng (6 chữ số)
                </label>
                <input
                  id="vietqr-bank-bin"
                  type="text"
                  inputMode="numeric"
                  disabled={!canEdit || isSaving}
                  value={formState.bankBin}
                  onChange={(e) => updateField('bankBin', e.target.value)}
                  aria-invalid={Boolean(fieldErrors.bankBin)}
                  aria-describedby={fieldErrors.bankBin ? 'err-bank-bin' : undefined}
                  placeholder="VD: 970422 (MB Bank), 970436 (VCB)"
                  className="h-10 w-full rounded-lg border border-border bg-white px-3 font-mono text-xs text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-none disabled:bg-canvas disabled:opacity-60"
                />
                {fieldErrors.bankBin && (
                  <p id="err-bank-bin" className="text-xs text-danger">
                    {fieldErrors.bankBin}
                  </p>
                )}
              </div>

              <div className="space-y-1.5">
                <label
                  htmlFor="vietqr-account-number"
                  className="block text-xs font-semibold text-ink"
                >
                  Số tài khoản nhận viện phí (6–19 chữ số)
                </label>
                <input
                  id="vietqr-account-number"
                  type="text"
                  inputMode="numeric"
                  disabled={!canEdit || isSaving}
                  value={formState.accountNumber}
                  onChange={(e) => updateField('accountNumber', e.target.value)}
                  aria-invalid={Boolean(fieldErrors.accountNumber)}
                  aria-describedby={fieldErrors.accountNumber ? 'err-account-number' : undefined}
                  placeholder="VD: 0123456789"
                  className="h-10 w-full rounded-lg border border-border bg-white px-3 font-mono text-xs text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-none disabled:bg-canvas disabled:opacity-60"
                />
                {fieldErrors.accountNumber && (
                  <p id="err-account-number" className="text-xs text-danger">
                    {fieldErrors.accountNumber}
                  </p>
                )}
              </div>

              <div className="space-y-1.5">
                <label
                  htmlFor="vietqr-account-name"
                  className="block text-xs font-semibold text-ink"
                >
                  Tên chủ tài khoản (viết hoa không dấu)
                </label>
                <input
                  id="vietqr-account-name"
                  type="text"
                  disabled={!canEdit || isSaving}
                  value={formState.accountName}
                  onChange={(e) => updateField('accountName', e.target.value)}
                  aria-invalid={Boolean(fieldErrors.accountName)}
                  aria-describedby={fieldErrors.accountName ? 'err-account-name' : undefined}
                  placeholder="VD: BENH VIEN MEDFLOW HA NOI"
                  className="h-10 w-full rounded-lg border border-border bg-white px-3 font-mono text-xs uppercase text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-none disabled:bg-canvas disabled:opacity-60"
                />
                {fieldErrors.accountName && (
                  <p id="err-account-name" className="text-xs text-danger">
                    {fieldErrors.accountName}
                  </p>
                )}
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div className="space-y-1.5">
                  <label
                    htmlFor="vietqr-transfer-prefix"
                    className="block text-xs font-semibold text-ink"
                  >
                    Tiền tố nội dung CK
                  </label>
                  <input
                    id="vietqr-transfer-prefix"
                    type="text"
                    disabled={!canEdit || isSaving}
                    value={formState.transferPrefix}
                    onChange={(e) => updateField('transferPrefix', e.target.value)}
                    aria-invalid={Boolean(fieldErrors.transferPrefix)}
                    className="h-10 w-full rounded-lg border border-border bg-white px-3 font-mono text-xs uppercase text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-none disabled:bg-canvas disabled:opacity-60"
                  />
                  {fieldErrors.transferPrefix && (
                    <p className="text-xs text-danger">{fieldErrors.transferPrefix}</p>
                  )}
                </div>

                <div className="space-y-1.5">
                  <label htmlFor="vietqr-expiry" className="block text-xs font-semibold text-ink">
                    Hạn mã QR (phút)
                  </label>
                  <input
                    id="vietqr-expiry"
                    type="number"
                    min={1}
                    max={120}
                    disabled={!canEdit || isSaving}
                    value={formState.pendingExpiryMinutes}
                    onChange={(e) => updateField('pendingExpiryMinutes', e.target.value)}
                    aria-invalid={Boolean(fieldErrors.pendingExpiryMinutes)}
                    className="h-10 w-full rounded-lg border border-border bg-white px-3 font-mono text-xs text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-none disabled:bg-canvas disabled:opacity-60"
                  />
                  {fieldErrors.pendingExpiryMinutes && (
                    <p className="text-xs text-danger">{fieldErrors.pendingExpiryMinutes}</p>
                  )}
                </div>
              </div>
            </div>
          )}

          {provider.providerKey === 'SMTP_EMAIL' && (
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              <div className="rounded-xl border border-border bg-canvas p-3 text-xs md:col-span-2">
                <span className="font-semibold text-sub">Máy chủ SMTP hiện hành (Runtime): </span>
                <span className="font-mono font-bold text-ink">
                  {provider.smtp?.host}:{provider.smtp?.port}
                </span>
                <span className="ml-3 text-sub">• Tài khoản gửi: </span>
                <span className="font-mono font-semibold text-ink">
                  {provider.smtp?.senderIdentity ?? 'Chưa cấu hình'}
                </span>
              </div>

              <div className="space-y-1.5">
                <label htmlFor="smtp-from-name" className="block text-xs font-semibold text-ink">
                  Tên hiển thị người gửi (From Name)
                </label>
                <input
                  id="smtp-from-name"
                  type="text"
                  disabled={!canEdit || isSaving}
                  value={formState.fromDisplayName}
                  onChange={(e) => updateField('fromDisplayName', e.target.value)}
                  aria-invalid={Boolean(fieldErrors.fromDisplayName)}
                  className="h-10 w-full rounded-lg border border-border bg-white px-3 text-xs text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-none disabled:bg-canvas disabled:opacity-60"
                />
                {fieldErrors.fromDisplayName && (
                  <p className="text-xs text-danger">{fieldErrors.fromDisplayName}</p>
                )}
              </div>

              <div className="space-y-1.5">
                <label htmlFor="smtp-reply-to" className="block text-xs font-semibold text-ink">
                  Địa chỉ Email phản hồi (Reply-To, tùy chọn)
                </label>
                <input
                  id="smtp-reply-to"
                  type="email"
                  disabled={!canEdit || isSaving}
                  value={formState.replyToEmail}
                  onChange={(e) => updateField('replyToEmail', e.target.value)}
                  aria-invalid={Boolean(fieldErrors.replyToEmail)}
                  placeholder="cskh@medflow.vn"
                  className="h-10 w-full rounded-lg border border-border bg-white px-3 font-mono text-xs text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-none disabled:bg-canvas disabled:opacity-60"
                />
                {fieldErrors.replyToEmail && (
                  <p className="text-xs text-danger">{fieldErrors.replyToEmail}</p>
                )}
              </div>

              <div className="space-y-1.5">
                <label
                  htmlFor="smtp-subject-prefix"
                  className="block text-xs font-semibold text-ink"
                >
                  Tiền tố tiêu đề Email
                </label>
                <input
                  id="smtp-subject-prefix"
                  type="text"
                  disabled={!canEdit || isSaving}
                  value={formState.subjectPrefix}
                  onChange={(e) => updateField('subjectPrefix', e.target.value)}
                  aria-invalid={Boolean(fieldErrors.subjectPrefix)}
                  className="h-10 w-full rounded-lg border border-border bg-white px-3 font-mono text-xs text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-none disabled:bg-canvas disabled:opacity-60"
                />
                {fieldErrors.subjectPrefix && (
                  <p className="text-xs text-danger">{fieldErrors.subjectPrefix}</p>
                )}
              </div>

              <div className="space-y-1.5">
                <label htmlFor="smtp-timeout" className="block text-xs font-semibold text-ink">
                  Thời gian chờ kết nối SMTP (5–120 giây)
                </label>
                <input
                  id="smtp-timeout"
                  type="number"
                  min={5}
                  max={120}
                  disabled={!canEdit || isSaving}
                  value={formState.smtpTimeoutSeconds}
                  onChange={(e) => updateField('smtpTimeoutSeconds', e.target.value)}
                  aria-invalid={Boolean(fieldErrors.smtpTimeoutSeconds)}
                  className="h-10 w-full rounded-lg border border-border bg-white px-3 font-mono text-xs text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-none disabled:bg-canvas disabled:opacity-60"
                />
                {fieldErrors.smtpTimeoutSeconds && (
                  <p className="text-xs text-danger">{fieldErrors.smtpTimeoutSeconds}</p>
                )}
              </div>
            </div>
          )}

          {provider.providerKey === 'AI_PROVIDER' && (
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              <div className="space-y-1.5">
                <label htmlFor="ai-provider-name" className="block text-xs font-semibold text-ink">
                  Chế độ / Nhà cung cấp AI
                </label>
                <select
                  id="ai-provider-name"
                  disabled={!canEdit || isSaving}
                  value={formState.providerName}
                  onChange={(e) => updateField('providerName', e.target.value)}
                  className="h-10 w-full rounded-lg border border-border bg-white px-3 font-mono text-xs text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-none disabled:bg-canvas disabled:opacity-60"
                >
                  <option value="GEMINI">GEMINI (Google Gemini API)</option>
                  <option value="OPENAI">OPENAI (OpenAI Compatible)</option>
                  <option value="DETERMINISTIC_CLINICAL">
                    DETERMINISTIC_CLINICAL (Bộ quy tắc lâm sàng nội bộ)
                  </option>
                </select>
                {fieldErrors.providerName && (
                  <p className="text-xs text-danger">{fieldErrors.providerName}</p>
                )}
              </div>

              <div className="space-y-1.5">
                <label htmlFor="ai-model-name" className="block text-xs font-semibold text-ink">
                  Tên mô hình (Model ID)
                </label>
                <input
                  id="ai-model-name"
                  type="text"
                  disabled={!canEdit || isSaving}
                  value={formState.modelName}
                  onChange={(e) => updateField('modelName', e.target.value)}
                  aria-invalid={Boolean(fieldErrors.modelName)}
                  className="h-10 w-full rounded-lg border border-border bg-white px-3 font-mono text-xs text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-none disabled:bg-canvas disabled:opacity-60"
                />
                {fieldErrors.modelName && (
                  <p className="text-xs text-danger">{fieldErrors.modelName}</p>
                )}
              </div>

              <div className="space-y-1.5">
                <label htmlFor="ai-timeout" className="block text-xs font-semibold text-ink">
                  Thời gian chờ AI tối đa (3–60 giây)
                </label>
                <input
                  id="ai-timeout"
                  type="number"
                  min={3}
                  max={60}
                  disabled={!canEdit || isSaving}
                  value={formState.aiTimeoutSeconds}
                  onChange={(e) => updateField('aiTimeoutSeconds', e.target.value)}
                  aria-invalid={Boolean(fieldErrors.aiTimeoutSeconds)}
                  className="h-10 w-full rounded-lg border border-border bg-white px-3 font-mono text-xs text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-none disabled:bg-canvas disabled:opacity-60"
                />
                {fieldErrors.aiTimeoutSeconds && (
                  <p className="text-xs text-danger">{fieldErrors.aiTimeoutSeconds}</p>
                )}
              </div>

              <div className="space-y-1.5">
                <label
                  htmlFor="ai-max-suggestions"
                  className="block text-xs font-semibold text-ink"
                >
                  Số lượng gợi ý tối đa (1–5)
                </label>
                <input
                  id="ai-max-suggestions"
                  type="number"
                  min={1}
                  max={5}
                  disabled={!canEdit || isSaving}
                  value={formState.maxSuggestions}
                  onChange={(e) => updateField('maxSuggestions', e.target.value)}
                  aria-invalid={Boolean(fieldErrors.maxSuggestions)}
                  className="h-10 w-full rounded-lg border border-border bg-white px-3 font-mono text-xs text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-none disabled:bg-canvas disabled:opacity-60"
                />
                {fieldErrors.maxSuggestions && (
                  <p className="text-xs text-danger">{fieldErrors.maxSuggestions}</p>
                )}
              </div>

              <div className="space-y-1.5 md:col-span-2">
                <label htmlFor="ai-disclaimer" className="block text-xs font-semibold text-ink">
                  Mẫu tuyên bố miễn trừ trách nhiệm y khoa
                </label>
                <textarea
                  id="ai-disclaimer"
                  rows={3}
                  disabled={!canEdit || isSaving}
                  value={formState.disclaimerTemplate}
                  onChange={(e) => updateField('disclaimerTemplate', e.target.value)}
                  aria-invalid={Boolean(fieldErrors.disclaimerTemplate)}
                  className="w-full rounded-lg border border-border bg-white p-3 text-xs text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-none disabled:bg-canvas disabled:opacity-60"
                />
                {fieldErrors.disclaimerTemplate && (
                  <p className="text-xs text-danger">{fieldErrors.disclaimerTemplate}</p>
                )}
              </div>
            </div>
          )}

          {/* Audit Reason */}
          {canEdit && (
            <div className="space-y-1.5 border-t border-border pt-4">
              <label htmlFor="integration-reason" className="block text-xs font-semibold text-ink">
                Ghi chú kiểm toán thay đổi (tùy chọn)
              </label>
              <input
                id="integration-reason"
                type="text"
                disabled={isSaving}
                value={formState.reason}
                onChange={(e) => updateField('reason', e.target.value)}
                placeholder="VD: Cập nhật tài khoản ngân hàng thu viện phí quý IV"
                className="h-10 w-full rounded-lg border border-border bg-white px-3 text-xs text-ink focus:border-primary focus:ring-2 focus:ring-primary/20 focus:outline-none disabled:bg-canvas disabled:opacity-60"
              />
              {fieldErrors.reason && <p className="text-xs text-danger">{fieldErrors.reason}</p>}
            </div>
          )}

          {!canEdit && (
            <Alert tone="info">
              Tài khoản hiện tại chỉ có quyền xem trạng thái tích hợp. Chỉ quản trị viên có quyền{' '}
              <code className="font-mono font-semibold">HOSPITAL_CONFIG</code> mới được phép cập
              nhật thông số.
            </Alert>
          )}
        </CardContent>

        {canEdit && (
          <CardFooter className="flex flex-wrap items-center justify-between gap-3 bg-canvas">
            <span className="text-xs text-sub">
              Mọi thay đổi tạo phiên bản mới trong{' '}
              <code className="font-mono">hospital_policies</code> và ghi sự kiện{' '}
              <code className="font-mono">INTEGRATION_CONFIG_UPDATED</code>.
            </span>

            <div className="flex items-center gap-2.5">
              <Button
                type="button"
                variant="secondary"
                size="sm"
                disabled={isSaving}
                onClick={handleReset}
              >
                <Undo2 className="h-3.5 w-3.5" aria-hidden="true" />
                <span>Hoàn tác</span>
              </Button>
              <Button type="submit" size="sm" loading={isSaving}>
                <Save className="h-4 w-4" aria-hidden="true" />
                <span>Lưu cấu hình tích hợp</span>
              </Button>
            </div>
          </CardFooter>
        )}
      </form>
    </Card>
  );
}
