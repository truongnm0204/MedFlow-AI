import { useState, type FormEvent } from 'react';
import { Plus } from 'lucide-react';

import { Badge, Button, Input } from '@/shared/ui';
import { Modal } from '@/shared/ui/modal';
import { getErrorMessage } from '@/shared/lib/get-error-message';

import { createUnverifiedPatientProfile } from '@/features/ekyc/api/ekyc.api';
import {
  PATIENT_UNVERIFIED_REASONS,
  PATIENT_UNVERIFIED_REASONS_REQUIRING_IDENTIFICATION,
} from '@/features/ekyc/types/ekyc.types';
import type {
  CreateUnverifiedPatientProfileResponse,
  PatientUnverifiedReason,
} from '@/features/ekyc/types/ekyc.types';

import {
  createUnverifiedPatientSchema,
  toCreateUnverifiedPatientPayload,
} from '../schemas/create-unverified-patient.schema';
import type { PatientGender } from '../types/create-patient.types';
import type { CreateUnverifiedPatientFormData } from '../types/create-unverified-patient.types';
import { PATIENT_UNVERIFIED_REASON_LABELS } from '../types/patient-identity.types';

interface CreateUnverifiedPatientModalProps {
  open: boolean;
  onClose: () => void;
  onCreated?: (result: CreateUnverifiedPatientProfileResponse) => void;
  /** Mở ngay luồng eKYC hỗ trợ cho hồ sơ vừa tạo (nếu trang cha hỗ trợ). */
  onStartEkyc?: (patientId: number, patientName: string) => void;
}

type FieldErrors = Partial<Record<keyof CreateUnverifiedPatientFormData, string>>;

const INITIAL_FORM: CreateUnverifiedPatientFormData = {
  fullName: '',
  dateOfBirth: '',
  gender: '',
  phone: '',
  email: '',
  address: '',
  identityCard: '',
  reason: '',
  note: '',
};

const GENDER_OPTIONS: { value: PatientGender | ''; label: string }[] = [
  { value: '', label: 'Chưa xác định' },
  { value: 'MALE', label: 'Nam' },
  { value: 'FEMALE', label: 'Nữ' },
  { value: 'OTHER', label: 'Khác' },
];

/**
 * Tạo hồ sơ bệnh nhân KHÔNG qua eKYC (ngoại lệ do chủ sở hữu phê duyệt).
 * Bắt buộc chọn lý do; các lý do từ chối/không khả dụng/hết hạn mức phải kèm số giấy tờ.
 */
export function CreateUnverifiedPatientModal({
  open,
  onClose,
  onCreated,
  onStartEkyc,
}: CreateUnverifiedPatientModalProps) {
  const [form, setForm] = useState<CreateUnverifiedPatientFormData>(INITIAL_FORM);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [created, setCreated] = useState<CreateUnverifiedPatientProfileResponse | null>(null);

  const setField = <K extends keyof CreateUnverifiedPatientFormData>(
    key: K,
    value: CreateUnverifiedPatientFormData[K],
  ) => {
    setForm((prev) => ({ ...prev, [key]: value }));
    setErrors((prev) => ({ ...prev, [key]: undefined }));
  };

  const reasonRequiresIdentityCard =
    form.reason !== '' && PATIENT_UNVERIFIED_REASONS_REQUIRING_IDENTIFICATION.includes(form.reason);

  const handleClose = () => {
    if (submitting) return;
    setForm(INITIAL_FORM);
    setErrors({});
    setSubmitError(null);
    setCreated(null);
    onClose();
  };

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    const parsed = createUnverifiedPatientSchema.safeParse(form);
    if (!parsed.success) {
      const next: FieldErrors = {};
      for (const issue of parsed.error.issues) {
        const key = issue.path[0] as keyof CreateUnverifiedPatientFormData;
        next[key] ??= issue.message;
      }
      setErrors(next);
      return;
    }

    setErrors({});
    setSubmitError(null);
    setSubmitting(true);
    try {
      const result = await createUnverifiedPatientProfile(
        toCreateUnverifiedPatientPayload(parsed.data),
      );
      setCreated(result);
      onCreated?.(result);
    } catch (error) {
      setSubmitError(
        getErrorMessage(error, 'Không thể tạo hồ sơ không xác minh. Vui lòng thử lại.'),
      );
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal open={open} onClose={handleClose} maxWidth="max-w-2xl">
      <div role="document" aria-labelledby="create-unverified-title">
        <div className="border-b border-border px-6 py-4 pr-14">
          <h2 id="create-unverified-title" className="text-lg font-bold text-navy">
            Tạo hồ sơ không xác minh
          </h2>
          <p className="text-xs text-sub">
            Dành cho trường hợp không thể xác minh điện tử tại quầy tiếp đón
          </p>
        </div>

        {created ? (
          <div className="space-y-4 px-6 py-8 text-center" role="status">
            <p className="text-sm text-sub">Đã tạo hồ sơ cho {created.fullName}</p>
            <p className="text-2xl font-bold text-navy">{created.patientCode}</p>
            <div className="flex flex-wrap items-center justify-center gap-2 text-sm text-navy">
              <span>Trạng thái danh tính:</span>
              <Badge tone="slate">Chưa xác minh</Badge>
            </div>
            <p className="text-xs text-sub">
              Lý do: {PATIENT_UNVERIFIED_REASON_LABELS[created.unverifiedReason]}
            </p>
            <div className="flex flex-wrap justify-center gap-3 pt-2">
              {onStartEkyc ? (
                <Button
                  type="button"
                  onClick={() => {
                    onStartEkyc(created.patientId, created.fullName);
                    handleClose();
                  }}
                >
                  Xác minh eKYC hỗ trợ
                </Button>
              ) : null}
              <Button
                type="button"
                variant={onStartEkyc ? 'secondary' : 'primary'}
                onClick={handleClose}
              >
                Đóng
              </Button>
            </div>
          </div>
        ) : (
          <form onSubmit={handleSubmit} noValidate className="space-y-6 px-6 py-5">
            <div className="max-h-[65vh] space-y-6 overflow-y-auto pr-1">
              <section className="space-y-3">
                <h3 className="border-b border-border pb-2 text-sm font-bold text-navy">
                  Thông tin bệnh nhân
                </h3>
                <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                  <Input
                    id="unverified-fullName"
                    label="Họ và tên"
                    value={form.fullName}
                    error={errors.fullName}
                    onChange={(event) => setField('fullName', event.target.value)}
                  />
                  <Input
                    id="unverified-dateOfBirth"
                    label="Ngày sinh"
                    type="date"
                    value={form.dateOfBirth}
                    error={errors.dateOfBirth}
                    onChange={(event) => setField('dateOfBirth', event.target.value)}
                  />
                  <div className="flex flex-col gap-2">
                    <label
                      htmlFor="unverified-gender"
                      className="text-xs font-semibold uppercase tracking-wide text-slate-700"
                    >
                      Giới tính
                    </label>
                    <select
                      id="unverified-gender"
                      value={form.gender}
                      onChange={(event) =>
                        setField('gender', event.target.value as PatientGender | '')
                      }
                      className="w-full rounded-lg border border-border bg-white px-4 py-3 text-sm text-ink shadow-sm focus:outline-none focus:ring-2 focus:ring-primary/40"
                    >
                      {GENDER_OPTIONS.map((option) => (
                        <option key={option.value} value={option.value}>
                          {option.label}
                        </option>
                      ))}
                    </select>
                  </div>
                  <Input
                    id="unverified-phone"
                    label="Số điện thoại"
                    type="tel"
                    inputMode="numeric"
                    maxLength={10}
                    value={form.phone}
                    error={errors.phone}
                    onChange={(event) => setField('phone', event.target.value)}
                  />
                  <Input
                    id="unverified-email"
                    label="Email"
                    type="email"
                    value={form.email}
                    error={errors.email}
                    onChange={(event) => setField('email', event.target.value)}
                  />
                  <div className="sm:col-span-2">
                    <Input
                      id="unverified-address"
                      label="Địa chỉ"
                      value={form.address}
                      error={errors.address}
                      onChange={(event) => setField('address', event.target.value)}
                    />
                  </div>
                  <div className="sm:col-span-2">
                    <Input
                      id="unverified-identityCard"
                      label={`CCCD/VNeID${reasonRequiresIdentityCard ? ' *' : ''}`}
                      inputMode="numeric"
                      maxLength={12}
                      value={form.identityCard}
                      error={errors.identityCard}
                      onChange={(event) => setField('identityCard', event.target.value)}
                    />
                    {reasonRequiresIdentityCard ? (
                      <p className="mt-1 text-xs text-amber-700">
                        Lý do đã chọn bắt buộc khai báo số CCCD/VNeID (12 chữ số).
                      </p>
                    ) : null}
                  </div>
                </div>
              </section>

              <section className="space-y-3">
                <h3 className="border-b border-border pb-2 text-sm font-bold text-navy">
                  Lý do tạo hồ sơ không xác minh
                </h3>
                <div className="grid grid-cols-1 gap-4">
                  <div className="flex flex-col gap-2">
                    <label
                      htmlFor="unverified-reason"
                      className="text-xs font-semibold uppercase tracking-wide text-slate-700"
                    >
                      Lý do
                    </label>
                    <select
                      id="unverified-reason"
                      value={form.reason}
                      onChange={(event) =>
                        setField('reason', event.target.value as PatientUnverifiedReason | '')
                      }
                      aria-invalid={!!errors.reason}
                      className={`w-full rounded-lg border bg-white px-4 py-3 text-sm text-ink shadow-sm focus:outline-none focus:ring-2 focus:ring-primary/40 ${
                        errors.reason ? 'border-danger' : 'border-border'
                      }`}
                    >
                      <option value="">Chọn lý do</option>
                      {PATIENT_UNVERIFIED_REASONS.map((reason) => (
                        <option key={reason} value={reason}>
                          {PATIENT_UNVERIFIED_REASON_LABELS[reason]}
                        </option>
                      ))}
                    </select>
                    {errors.reason ? <p className="text-xs text-danger">{errors.reason}</p> : null}
                  </div>
                  <div className="flex flex-col gap-2">
                    <label
                      htmlFor="unverified-note"
                      className="text-xs font-semibold uppercase tracking-wide text-slate-700"
                    >
                      Ghi chú
                    </label>
                    <textarea
                      id="unverified-note"
                      rows={3}
                      value={form.note}
                      onChange={(event) => setField('note', event.target.value)}
                      placeholder="Thông tin bổ sung cho hồ sơ ngoại lệ (không bắt buộc)"
                      className="w-full rounded-lg border border-border bg-white px-4 py-3 text-sm text-ink shadow-sm placeholder:text-slate-400 focus:outline-none focus:ring-2 focus:ring-primary/40"
                    />
                  </div>
                </div>
              </section>
            </div>

            {submitError ? (
              <p role="alert" className="text-sm text-danger">
                {submitError}
              </p>
            ) : null}

            <div className="flex justify-end gap-3 border-t border-border pt-4">
              <Button type="button" variant="secondary" onClick={handleClose} disabled={submitting}>
                Hủy bỏ
              </Button>
              <Button type="submit" loading={submitting}>
                {!submitting && <Plus className="h-4 w-4" aria-hidden="true" />}
                Tạo hồ sơ không xác minh
              </Button>
            </div>
          </form>
        )}
      </div>
    </Modal>
  );
}
