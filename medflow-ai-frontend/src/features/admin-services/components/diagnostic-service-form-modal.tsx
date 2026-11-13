import { useState, type FormEvent, type ReactElement } from 'react';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Alert } from '@/shared/ui/alert';
import { Button } from '@/shared/ui/button';
import { Modal } from '@/shared/ui/modal';

import {
  useCreateDiagnosticService,
  useDiagnosticServiceFormOptions,
  useUpdateDiagnosticService,
} from '../hooks/use-diagnostic-services';
import {
  diagnosticServiceFormSchema,
  toCreateDiagnosticServicePayload,
  toUpdateDiagnosticServicePayload,
} from '../schemas/diagnostic-service-form.schema';
import type {
  DiagnosticServiceDepartmentFormOption,
  DiagnosticServiceItem,
  DiagnosticServiceSpecialtyFormOption,
} from '../types/diagnostic-service.types';

interface DiagnosticServiceFormModalProps {
  open: boolean;
  mode: 'create' | 'edit';
  service: DiagnosticServiceItem | null;
  onClose: () => void;
  onSaved: (item: DiagnosticServiceItem) => void;
}

const fieldClass =
  'w-full rounded-lg border border-border bg-white px-3 py-2 text-sm text-ink outline-none focus:border-navy disabled:bg-canvas disabled:text-sub';

export function DiagnosticServiceFormModal(props: DiagnosticServiceFormModalProps) {
  if (!props.open) return null;
  return <DiagnosticServiceForm key={`${props.mode}-${props.service?.id ?? 'new'}`} {...props} />;
}

function DiagnosticServiceForm({
  open,
  mode,
  service,
  onClose,
  onSaved,
}: DiagnosticServiceFormModalProps) {
  const optionsQuery = useDiagnosticServiceFormOptions(open);
  const createMutation = useCreateDiagnosticService();
  const updateMutation = useUpdateDiagnosticService();
  const pending = createMutation.isPending || updateMutation.isPending;

  const [serviceCode, setServiceCode] = useState(
    mode === 'edit' ? (service?.serviceCode ?? '') : '',
  );
  const [name, setName] = useState(mode === 'edit' ? (service?.name ?? '') : '');
  const [description, setDescription] = useState(
    mode === 'edit' ? (service?.description ?? '') : '',
  );
  const [departmentId, setDepartmentId] = useState(
    mode === 'edit' && service?.departmentId ? String(service.departmentId) : '',
  );
  const [specialtyId, setSpecialtyId] = useState(
    mode === 'edit' && service?.specialtyId ? String(service.specialtyId) : '',
  );
  const [requiresResultVerification, setRequiresResultVerification] = useState(
    mode === 'edit' ? Boolean(service?.requiresResultVerification) : true,
  );
  const [price, setPrice] = useState('');
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [submitError, setSubmitError] = useState<string | null>(null);

  const departments = withCurrentOption(
    optionsQuery.data?.departments ?? [],
    mode === 'edit' ? service?.departmentId : null,
    service?.departmentName ?? null,
    service?.departmentCode ?? null,
  );
  const specialties = withCurrentOption(
    optionsQuery.data?.specialties ?? [],
    mode === 'edit' ? service?.specialtyId : null,
    service?.specialtyName ?? null,
    service?.specialtyCode ?? null,
  );

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    const parsed = diagnosticServiceFormSchema.safeParse({
      serviceCode,
      name,
      description,
      departmentId,
      specialtyId,
      requiresResultVerification,
      price: mode === 'create' ? price : '',
    });
    if (!parsed.success) {
      const next: Record<string, string> = {};
      for (const issue of parsed.error.issues) {
        const key = String(issue.path[0] ?? '');
        if (key && !next[key]) next[key] = issue.message;
      }
      setFieldErrors(next);
      return;
    }

    setFieldErrors({});
    setSubmitError(null);
    try {
      const item =
        mode === 'create'
          ? await createMutation.mutateAsync(toCreateDiagnosticServicePayload(parsed.data))
          : await updateMutation.mutateAsync({
              id: service?.id ?? 0,
              payload: toUpdateDiagnosticServicePayload(parsed.data),
            });
      onSaved(item);
    } catch (error) {
      setSubmitError(
        getErrorMessage(error, 'Không thể lưu dịch vụ cận lâm sàng. Vui lòng thử lại.'),
      );
    }
  };

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-xl">
      <form onSubmit={(event) => void handleSubmit(event)} className="space-y-4 p-6">
        <h2 className="pr-8 text-base font-bold text-ink">
          {mode === 'create' ? 'Thêm dịch vụ cận lâm sàng' : 'Cập nhật dịch vụ cận lâm sàng'}
        </h2>

        {optionsQuery.isPending && (
          <p role="status" className="text-xs text-sub">
            Đang tải danh mục khoa phòng…
          </p>
        )}
        {optionsQuery.isError && (
          <Alert tone="error">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <span>Không tải được danh mục khoa phòng.</span>
              <Button
                type="button"
                variant="secondary"
                size="sm"
                onClick={() => void optionsQuery.refetch()}
              >
                Thử lại
              </Button>
            </div>
          </Alert>
        )}
        {submitError && <Alert tone="error">{submitError}</Alert>}

        <Field label="Mã dịch vụ" error={fieldErrors.serviceCode}>
          <input
            id="diagnostic-service-code"
            className={fieldClass}
            value={serviceCode}
            disabled={mode === 'edit'}
            onChange={(event) => setServiceCode(event.target.value)}
            aria-invalid={Boolean(fieldErrors.serviceCode)}
          />
        </Field>
        <Field label="Tên dịch vụ" error={fieldErrors.name}>
          <input
            id="diagnostic-service-name"
            className={fieldClass}
            value={name}
            onChange={(event) => setName(event.target.value)}
            aria-invalid={Boolean(fieldErrors.name)}
          />
        </Field>
        <Field label="Mô tả" error={fieldErrors.description}>
          <textarea
            id="diagnostic-service-description"
            className={fieldClass}
            rows={3}
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            aria-invalid={Boolean(fieldErrors.description)}
          />
        </Field>
        <Field label="Khoa thực hiện" error={fieldErrors.departmentId}>
          <select
            id="diagnostic-service-department"
            className={fieldClass}
            value={departmentId}
            onChange={(event) => setDepartmentId(event.target.value)}
            aria-invalid={Boolean(fieldErrors.departmentId)}
          >
            <option value="">Chọn khoa</option>
            {departments.map((department) => (
              <option key={department.id} value={department.id}>
                {department.name}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Chuyên khoa" error={fieldErrors.specialtyId}>
          <select
            id="diagnostic-service-specialty"
            className={fieldClass}
            value={specialtyId}
            onChange={(event) => setSpecialtyId(event.target.value)}
            aria-invalid={Boolean(fieldErrors.specialtyId)}
          >
            <option value="">Không chọn</option>
            {specialties.map((specialty) => (
              <option key={specialty.id} value={specialty.id}>
                {specialty.name}
              </option>
            ))}
          </select>
        </Field>
        <label
          className="flex items-center gap-2 text-sm text-ink"
          htmlFor="diagnostic-service-verify"
        >
          <input
            id="diagnostic-service-verify"
            type="checkbox"
            checked={requiresResultVerification}
            onChange={(event) => setRequiresResultVerification(event.target.checked)}
          />
          Yêu cầu xác nhận kết quả
        </label>
        {mode === 'create' ? (
          <Field label="Đơn giá (đ)" error={fieldErrors.price}>
            <input
              id="diagnostic-service-price"
              className={fieldClass}
              inputMode="decimal"
              value={price}
              onChange={(event) => setPrice(event.target.value)}
              aria-invalid={Boolean(fieldErrors.price)}
            />
          </Field>
        ) : (
          <p className="text-xs text-sub">
            Đơn giá hiện tại: {service?.priceFormatted ?? 'Chưa có bảng giá'} — đổi giá tại màn Bảng
            giá dịch vụ.
          </p>
        )}

        <div className="flex justify-end gap-2 pt-2">
          <Button type="button" variant="secondary" onClick={onClose}>
            Huỷ
          </Button>
          <Button type="submit" disabled={pending}>
            Lưu dịch vụ
          </Button>
        </div>
      </form>
    </Modal>
  );
}

function Field({
  label,
  error,
  children,
}: {
  label: string;
  error?: string;
  children: ReactElement<{ id: string }>;
}) {
  const id = children.props.id;
  return (
    <div>
      <label htmlFor={id} className="mb-1 block text-xs font-semibold text-ink">
        {label}
      </label>
      {children}
      {error && (
        <p role="alert" className="mt-1 text-xs text-rose-700">
          {error}
        </p>
      )}
    </div>
  );
}

function withCurrentOption<T extends { id: number; name: string }>(
  options: T[],
  currentId: number | null | undefined,
  currentName: string | null,
  currentCode: string | null,
): T[] {
  if (!currentId || !currentName || options.some((option) => option.id === currentId)) {
    return options;
  }
  return [
    ...options,
    {
      id: currentId,
      name: `${currentName} (ngừng hoạt động)`,
      departmentCode: currentCode,
      specialtyCode: currentCode,
    } as T & DiagnosticServiceDepartmentFormOption & DiagnosticServiceSpecialtyFormOption,
  ];
}
