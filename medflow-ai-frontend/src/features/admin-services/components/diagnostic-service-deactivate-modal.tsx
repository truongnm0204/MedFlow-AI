import { AlertTriangle } from 'lucide-react';
import { useState, type FormEvent } from 'react';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';
import { Modal } from '@/shared/ui/modal';

import { useDeactivateDiagnosticService } from '../hooks/use-diagnostic-services';
import { deactivateDiagnosticServiceSchema } from '../schemas/diagnostic-service-form.schema';
import type { DiagnosticServiceItem } from '../types/diagnostic-service.types';

interface DiagnosticServiceDeactivateModalProps {
  open: boolean;
  service: DiagnosticServiceItem | null;
  onClose: () => void;
  onDeactivated: (item: DiagnosticServiceItem) => void;
}

export function DiagnosticServiceDeactivateModal(props: DiagnosticServiceDeactivateModalProps) {
  if (!props.open || !props.service) return null;
  return (
    <DiagnosticServiceDeactivateForm key={props.service.id} {...props} service={props.service} />
  );
}

function DiagnosticServiceDeactivateForm({
  open,
  service,
  onClose,
  onDeactivated,
}: DiagnosticServiceDeactivateModalProps & { service: DiagnosticServiceItem }) {
  const mutation = useDeactivateDiagnosticService();
  const [reason, setReason] = useState('');
  const [fieldError, setFieldError] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    if (!service) return;
    const parsed = deactivateDiagnosticServiceSchema.safeParse({ reason });
    if (!parsed.success) {
      setFieldError(
        parsed.error.issues[0]?.message ?? 'Lý do ngừng hoạt động phải từ 5 đến 500 ký tự.',
      );
      return;
    }
    setFieldError(null);
    setSubmitError(null);
    try {
      const item = await mutation.mutateAsync({
        id: service.id,
        payload: { reason: parsed.data.reason },
      });
      onDeactivated(item);
    } catch (error) {
      setSubmitError(
        getErrorMessage(error, 'Không thể ngừng hoạt động dịch vụ. Vui lòng thử lại.'),
      );
    }
  };

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-lg">
      <form onSubmit={(event) => void handleSubmit(event)} className="space-y-4 p-6">
        <div className="flex items-start gap-3 pr-8">
          <AlertTriangle aria-hidden="true" className="mt-0.5 h-5 w-5 shrink-0 text-rose-700" />
          <div>
            <h2 className="text-base font-bold text-ink">Ngừng hoạt động dịch vụ</h2>
            {service && (
              <p className="mt-1 text-sm text-sub">
                {service.name} · {service.serviceCode}
              </p>
            )}
          </div>
        </div>

        {service && service.activeServicePointCount > 0 && (
          <Alert tone="info">
            Dịch vụ đang gắn {service.activeServicePointCount} điểm phục vụ. Điểm phục vụ giữ
            nguyên, bác sĩ sẽ không chỉ định được dịch vụ này.
          </Alert>
        )}
        {submitError && <Alert tone="error">{submitError}</Alert>}

        <div>
          <label
            htmlFor="diagnostic-deactivate-reason"
            className="mb-1 block text-xs font-semibold text-ink"
          >
            Lý do ngừng hoạt động
          </label>
          <textarea
            id="diagnostic-deactivate-reason"
            className="w-full rounded-lg border border-border bg-white px-3 py-2 text-sm text-ink outline-none focus:border-navy"
            rows={3}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            aria-invalid={Boolean(fieldError)}
          />
          {fieldError && (
            <p role="alert" className="mt-1 text-xs text-rose-700">
              {fieldError}
            </p>
          )}
        </div>

        <div className="flex justify-end gap-2">
          <Button type="button" variant="secondary" onClick={onClose}>
            Huỷ
          </Button>
          <Button type="submit" variant="danger" disabled={mutation.isPending}>
            Xác nhận ngừng hoạt động
          </Button>
        </div>
      </form>
    </Modal>
  );
}
