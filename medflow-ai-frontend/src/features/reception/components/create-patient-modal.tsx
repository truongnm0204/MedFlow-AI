import { useState, type FormEvent } from 'react';
import { Plus } from 'lucide-react';

import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Modal } from '@/shared/ui/modal';

import type { FinalizeEkycSessionResponse } from '@/features/ekyc/types/ekyc.types';

import { fetchPatientDetail } from '../api/reception.api';
import { createPatientSchema } from '../schemas/create-patient.schema';
import type {
  CreatePatientFormData,
  CreatedPatientResult,
  PatientGender,
} from '../types/create-patient.types';
import { CreateUnverifiedPatientModal } from './create-unverified-patient-modal';
import { EkycAssistedModal } from './ekyc-assisted-modal';

interface CreatePatientModalProps {
  open: boolean;
  onClose: () => void;
  onSubmit: (data: CreatePatientFormData) => Promise<CreatedPatientResult>;
  onCreated?: (result: CreatedPatientResult) => void;
  /** Được gọi khi hồ sơ vừa tạo hoàn tất xác minh eKYC để trang cha tải lại. */
  onEkycVerified?: (result: FinalizeEkycSessionResponse) => void;
}

type FieldErrors = Partial<Record<keyof CreatePatientFormData, string>>;

const INITIAL_FORM: CreatePatientFormData = {
  identityCard: '',
  fullName: '',
  dateOfBirth: '',
  gender: 'MALE',
  ethnicity: 'Kinh',
  phone: '',
  email: '',
  address: '',
  insuranceNumber: '',
  insuranceRegisteredPlace: '',
  emergencyContactName: '',
  emergencyContactRelationship: '',
  emergencyContactPhone: '',
};

const GENDER_OPTIONS: { value: PatientGender; label: string }[] = [
  { value: 'MALE', label: 'Nam' },
  { value: 'FEMALE', label: 'Nữ' },
  { value: 'OTHER', label: 'Khác' },
];

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="space-y-3">
      <h3 className="border-b border-border pb-2 text-sm font-bold text-navy">{title}</h3>
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">{children}</div>
    </section>
  );
}

export function CreatePatientModal({
  open,
  onClose,
  onSubmit,
  onCreated,
  onEkycVerified,
}: CreatePatientModalProps) {
  const [form, setForm] = useState<CreatePatientFormData>(INITIAL_FORM);
  const [errors, setErrors] = useState<FieldErrors>({});
  const [submitting, setSubmitting] = useState(false);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [created, setCreated] = useState<CreatedPatientResult | null>(null);
  const [unverifiedOpen, setUnverifiedOpen] = useState(false);
  const [ekycTarget, setEkycTarget] = useState<{
    patientId: number;
    patientName: string;
  } | null>(null);
  const [ekycLoading, setEkycLoading] = useState(false);
  const [ekycError, setEkycError] = useState<string | null>(null);

  const setField = <K extends keyof CreatePatientFormData>(
    key: K,
    value: CreatePatientFormData[K],
  ) => setForm((prev) => ({ ...prev, [key]: value }));

  const handleClose = () => {
    if (submitting) return;
    setForm(INITIAL_FORM);
    setErrors({});
    setSubmitError(null);
    setCreated(null);
    setUnverifiedOpen(false);
    setEkycTarget(null);
    setEkycLoading(false);
    setEkycError(null);
    onClose();
  };

  const handleStartEkyc = async () => {
    if (!created) return;
    setEkycLoading(true);
    setEkycError(null);
    try {
      const detail = await fetchPatientDetail(created.patientCode);
      const patientId = Number(detail.id);
      if (!Number.isInteger(patientId) || patientId <= 0) {
        setEkycError('Không xác định được hồ sơ để bắt đầu xác minh. Vui lòng thử lại.');
        return;
      }
      setEkycTarget({ patientId, patientName: created.fullName });
    } catch {
      setEkycError('Không thể mở phiên xác minh cho hồ sơ vừa tạo. Vui lòng thử lại.');
    } finally {
      setEkycLoading(false);
    }
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    const parsed = createPatientSchema.safeParse(form);
    if (!parsed.success) {
      const next: FieldErrors = {};
      for (const issue of parsed.error.issues) {
        const key = issue.path[0] as keyof CreatePatientFormData;
        next[key] ??= issue.message;
      }
      setErrors(next);
      return;
    }
    setErrors({});
    setSubmitError(null);
    setSubmitting(true);
    try {
      const result = await onSubmit(parsed.data as CreatePatientFormData);
      setCreated(result);
      onCreated?.(result);
    } catch {
      setSubmitError('Không thể tạo hồ sơ bệnh nhân. Vui lòng thử lại.');
    } finally {
      setSubmitting(false);
    }
  };

  const field = (
    key: keyof CreatePatientFormData,
    label: string,
    extra: React.InputHTMLAttributes<HTMLInputElement> = {},
  ) => (
    <Input
      id={`patient-${key}`}
      label={label}
      value={form[key]}
      error={errors[key]}
      onChange={(e) => setField(key, e.target.value as never)}
      {...extra}
    />
  );

  return (
    <Modal open={open} onClose={handleClose} maxWidth="max-w-3xl">
      <>
        <div role="document" aria-labelledby="create-patient-title">
          <div className="border-b border-border px-6 py-4">
            <h2 id="create-patient-title" className="text-lg font-bold text-navy">
              Tạo hồ sơ bệnh nhân
            </h2>
            <p className="text-xs text-sub">Nhập thông tin bệnh nhân mới tại quầy tiếp đón</p>
          </div>

          {created ? (
            <div className="space-y-4 px-6 py-8 text-center" role="status">
              <p className="text-sm text-sub">Đã tạo hồ sơ cho {created.fullName}</p>
              <p className="text-2xl font-bold text-navy">{created.patientCode}</p>
              <p className="text-xs text-sub">
                Hồ sơ mới được tạo ở trạng thái chưa xác minh danh tính. Có thể xác minh ngay bằng
                eKYC do lễ tân hỗ trợ.
              </p>
              {ekycError ? (
                <p role="alert" className="text-sm text-danger">
                  {ekycError}
                </p>
              ) : null}
              <div className="flex flex-wrap justify-center gap-3">
                <Button
                  type="button"
                  variant="secondary"
                  onClick={() => {
                    void handleStartEkyc();
                  }}
                  loading={ekycLoading}
                >
                  Xác minh eKYC hỗ trợ
                </Button>
                <Button type="button" aria-label="Đóng" onClick={handleClose}>
                  Đóng
                </Button>
              </div>
            </div>
          ) : (
            <form onSubmit={handleSubmit} noValidate className="space-y-6 px-6 py-5">
              <div className="max-h-[65vh] space-y-6 overflow-y-auto pr-1">
                <Section title="Thông tin định danh">
                  {field('identityCard', 'CCCD/VNeID', { inputMode: 'numeric', maxLength: 12 })}
                  {field('fullName', 'Họ và tên')}
                  {field('dateOfBirth', 'Ngày sinh', { type: 'date' })}
                  <div className="flex flex-col gap-2">
                    <label
                      htmlFor="patient-gender"
                      className="text-xs font-semibold uppercase tracking-wide text-slate-700"
                    >
                      Giới tính
                    </label>
                    <select
                      id="patient-gender"
                      value={form.gender}
                      onChange={(e) => setField('gender', e.target.value as PatientGender)}
                      className="w-full rounded-lg border border-border bg-white px-4 py-3 text-sm text-ink shadow-sm focus:outline-none focus:ring-2 focus:ring-primary/40"
                    >
                      {GENDER_OPTIONS.map((o) => (
                        <option key={o.value} value={o.value}>
                          {o.label}
                        </option>
                      ))}
                    </select>
                  </div>
                  {field('ethnicity', 'Dân tộc')}
                </Section>

                <Section title="Thông tin liên hệ">
                  {field('phone', 'Số điện thoại', {
                    type: 'tel',
                    inputMode: 'numeric',
                    maxLength: 10,
                  })}
                  {field('email', 'Email', { type: 'email' })}
                  <div className="sm:col-span-2">{field('address', 'Địa chỉ thường trú')}</div>
                </Section>

                <Section title="Bảo hiểm y tế">
                  {field('insuranceNumber', 'Mã số thẻ BHYT', { maxLength: 15 })}
                  {field('insuranceRegisteredPlace', 'Nơi ĐKKCB ban đầu')}
                </Section>

                <Section title="Người liên hệ khẩn cấp">
                  {field('emergencyContactName', 'Họ tên')}
                  {field('emergencyContactRelationship', 'Mối quan hệ')}
                  {field('emergencyContactPhone', 'Số điện thoại người liên hệ', {
                    type: 'tel',
                    inputMode: 'numeric',
                    maxLength: 10,
                  })}
                </Section>
              </div>

              {submitError && (
                <p role="alert" className="text-sm text-danger">
                  {submitError}
                </p>
              )}

              <div className="flex justify-end gap-3 border-t border-border pt-4">
                <Button
                  type="button"
                  variant="secondary"
                  aria-label="Hủy bỏ"
                  onClick={handleClose}
                  disabled={submitting}
                >
                  Hủy bỏ
                </Button>
                <Button type="submit" aria-label="Tạo hồ sơ bệnh nhân" loading={submitting}>
                  {!submitting && <Plus className="h-4 w-4" aria-hidden="true" />}
                  Tạo hồ sơ bệnh nhân
                </Button>
              </div>

              <div className="flex flex-col gap-3 rounded-xl border border-dashed border-border px-4 py-3 sm:flex-row sm:items-center sm:justify-between">
                <div className="min-w-0">
                  <p className="text-sm font-semibold text-navy">Không thể xác minh điện tử?</p>
                  <p className="text-xs text-sub">
                    Tạo hồ sơ ở trạng thái chưa xác minh kèm lý do ngoại lệ (trẻ em, người nước
                    ngoài, không có giấy tờ hợp lệ...).
                  </p>
                </div>
                <Button
                  type="button"
                  variant="secondary"
                  aria-label="Tạo hồ sơ không xác minh"
                  onClick={() => setUnverifiedOpen(true)}
                  disabled={submitting}
                >
                  Tạo hồ sơ không xác minh
                </Button>
              </div>
            </form>
          )}
        </div>

        <CreateUnverifiedPatientModal
          open={unverifiedOpen}
          onClose={() => setUnverifiedOpen(false)}
          onCreated={(result) => {
            setCreated({ patientCode: result.patientCode, fullName: result.fullName });
            onCreated?.({ patientCode: result.patientCode, fullName: result.fullName });
          }}
          onStartEkyc={(patientId, patientName) => {
            setUnverifiedOpen(false);
            setEkycTarget({ patientId, patientName });
          }}
        />

        {ekycTarget ? (
          <EkycAssistedModal
            open
            patientId={ekycTarget.patientId}
            patientName={ekycTarget.patientName}
            onClose={() => setEkycTarget(null)}
            onVerified={(result) => onEkycVerified?.(result)}
          />
        ) : null}
      </>
    </Modal>
  );
}
