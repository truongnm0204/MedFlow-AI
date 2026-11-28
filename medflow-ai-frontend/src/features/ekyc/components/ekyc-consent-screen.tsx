import { Button, Card, CardContent, CardFooter, CardHeader } from '@/shared/ui';

/**
 * Bước đồng ý xác thực danh tính điện tử (eKYC).
 * Bắt buộc hiển thị và thu nhận đồng ý trước khi chụp/tải lên bất kỳ hình ảnh nào
 * (Luật 91/2025 và Nghị định 356/2025). Component thuần hiển thị, không tự giữ trạng thái.
 */
export interface EkycConsentScreenProps {
  /** Phiên bản điều khoản đồng ý — hiển thị và được gửi lại kèm yêu cầu tạo phiên. */
  consentVersion: string;
  /** Toàn bộ nội dung điều khoản đồng ý. */
  consentText: string;
  /** Trạng thái tích chọn đồng ý do component cha quản lý. */
  accepted: boolean;
  onAcceptedChange: (accepted: boolean) => void;
  /** Được gọi khi người dùng xác nhận và đã tích đồng ý. */
  onConfirm: () => void;
  onCancel?: () => void;
  /** Đang gửi yêu cầu — vô hiệu hóa thao tác và hiển thị trạng thái xử lý. */
  submitting?: boolean;
}

const CONSENT_CHECKBOX_ID = 'ekyc-consent-checkbox';
const CONSENT_HEADING_ID = 'ekyc-consent-heading';
const CONSENT_TEXT_ID = 'ekyc-consent-text';

export function EkycConsentScreen({
  consentVersion,
  consentText,
  accepted,
  onAcceptedChange,
  onConfirm,
  onCancel,
  submitting = false,
}: EkycConsentScreenProps) {
  return (
    <Card className="mx-auto w-full max-w-2xl" role="region" aria-labelledby={CONSENT_HEADING_ID}>
      <CardHeader>
        <h2 id={CONSENT_HEADING_ID} className="text-base font-bold text-ink">
          Đồng ý xác thực danh tính điện tử (eKYC)
        </h2>
        <p className="mt-1 text-sm text-sub">
          Vui lòng đọc kỹ các điều khoản bên dưới trước khi tiếp tục.
        </p>
      </CardHeader>

      <CardContent className="space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-2">
          <span className="text-xs font-semibold uppercase tracking-wide text-sub">
            Phiên bản điều khoản
          </span>
          <span className="rounded bg-canvas px-2 py-0.5 font-mono text-[11px] font-bold text-primary">
            {consentVersion}
          </span>
        </div>

        <div
          id={CONSENT_TEXT_ID}
          tabIndex={0}
          role="document"
          aria-label="Nội dung điều khoản đồng ý eKYC"
          className="max-h-64 overflow-y-auto whitespace-pre-line rounded-xl border border-border bg-canvas p-4 text-sm leading-relaxed text-ink focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40"
        >
          {consentText}
        </div>

        <label
          htmlFor={CONSENT_CHECKBOX_ID}
          className="flex cursor-pointer items-start gap-3 rounded-xl border border-border p-3.5 text-sm text-ink transition-colors hover:bg-canvas"
        >
          <input
            id={CONSENT_CHECKBOX_ID}
            type="checkbox"
            checked={accepted}
            disabled={submitting}
            onChange={(event) => onAcceptedChange(event.target.checked)}
            aria-describedby={CONSENT_TEXT_ID}
            className="mt-0.5 h-4 w-4 shrink-0 rounded border-border text-primary focus:ring-primary disabled:cursor-not-allowed disabled:opacity-60"
          />
          <span>Tôi đã đọc và đồng ý với các điều khoản trên</span>
        </label>
      </CardContent>

      <CardFooter className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
        {onCancel ? (
          <Button type="button" variant="secondary" onClick={onCancel} disabled={submitting}>
            Hủy
          </Button>
        ) : null}
        <Button
          type="button"
          onClick={onConfirm}
          disabled={!accepted || submitting}
          loading={submitting}
          aria-busy={submitting}
        >
          {submitting ? 'Đang xử lý…' : 'Đồng ý và tiếp tục'}
        </Button>
      </CardFooter>
    </Card>
  );
}
