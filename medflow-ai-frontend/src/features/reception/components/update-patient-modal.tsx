import { useState, type ChangeEvent, type FormEvent } from 'react';

import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Modal } from '@/shared/ui/modal';

import { updatePatientSchema } from '../schemas/update-patient.schema';
import type {
  PatientEditableFields,
  PatientGender,
  UpdatePatientFormData,
} from '../types/update-patient.types';

interface UpdatePatientModalProps {
  open: boolean;
  patient: UpdatePatientFormData;
  onClose: () => void;
  onSubmit: (patientCode: string, values: PatientEditableFields) => void | Promise<void>;
}

type FieldErrors = Partial<Record<keyof PatientEditableFields, string>>;

const GENDER_OPTIONS: { value: PatientGender; label: string }[] = [
  { value: 'MALE', label: 'Nam' },
  { value: 'FEMALE', label: 'Nữ' },
  { value: 'OTHER', label: 'Khác' },
];

function toEditable(patient: UpdatePatientFormData): PatientEditableFields {
  // eslint-disable-next-line @typescript-eslint/no-unused-vars
  const { patientCode: _code, ...rest } = patient;
  return rest;
}

export function UpdatePatientModal({ open, patient, onClose, onSubmit }: UpdatePatientModalProps) {
  const [values, setValues] = useState<PatientEditableFields>(() => toEditable(patient));
  const [errors, setErrors] = useState<FieldErrors>({});
  const [submitting, setSubmitting] = useState(false);

  const bind = (name: keyof PatientEditableFields) => ({
    name,
    id: `update-patient-${name}`,
    value: values[name],
    error: errors[name],
    onChange: (e: ChangeEvent<HTMLInputElement>) => {
      const next = e.target.value;
      setValues((v) => ({ ...v, [name]: next }));
      setErrors((er) => ({ ...er, [name]: undefined }));
    },
  });

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const result = updatePatientSchema.safeParse(values);
    if (!result.success) {
      const next: FieldErrors = {};
      for (const issue of result.error.issues) {
        const key = issue.path[0] as keyof PatientEditableFields | undefined;
        if (key && !next[key]) next[key] = issue.message;
      }
      setErrors(next);
      return;
    }
    setErrors({});
    setSubmitting(true);
    try {
      await onSubmit(patient.patientCode, result.data);
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <Modal open={open} onClose={onClose} maxWidth="max-w-3xl">
      <form
        onSubmit={handleSubmit}
        noValidate
        aria-labelledby="update-patient-title"
        className="max-h-[90vh] overflow-y-auto p-6"
      >
        <h2 id="update-patient-title" className="text-lg font-bold text-navy">
          Cập nhật hồ sơ bệnh nhân
        </h2>
        <p className="mt-0.5 text-xs text-sub">Chỉnh sửa thông tin hành chính của bệnh nhân.</p>

        <div className="mt-5 flex items-center gap-3">
          <span className="text-xs font-semibold uppercase tracking-wide text-slate-700">
            Mã bệnh nhân
          </span>
          <span
            data-testid="patient-code"
            aria-disabled="true"
            className="rounded-lg border border-border bg-canvas px-3 py-1.5 font-mono text-sm font-bold text-sub"
          >
            {patient.patientCode}
          </span>
        </div>

        <div className="mt-4 grid grid-cols-1 gap-4 md:grid-cols-2">
          <div className="md:col-span-2">
            <Input label="Họ và tên" {...bind('fullName')} />
          </div>
          <Input label="Ngày sinh" type="date" {...bind('dateOfBirth')} />
          <div className="flex flex-col gap-2">
            <label
              htmlFor="update-patient-gender"
              className="text-xs font-semibold uppercase tracking-wide text-slate-700"
            >
              Giới tính
            </label>
            <select
              id="update-patient-gender"
              value={values.gender}
              onChange={(e) =>
                setValues((v) => ({ ...v, gender: e.target.value as PatientGender }))
              }
              className="w-full rounded-lg border border-border bg-white px-4 py-3 text-sm text-ink shadow-sm focus:outline-none focus:ring-2 focus:ring-primary/40"
            >
              {GENDER_OPTIONS.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </select>
          </div>
          <Input label="Dân tộc" {...bind('ethnicity')} />
          <Input label="Số CCCD/VNeID" inputMode="numeric" {...bind('nationalId')} />
          <Input label="Số điện thoại" inputMode="tel" {...bind('phone')} />
          <Input label="Email" type="email" {...bind('email')} />
          <div className="md:col-span-2">
            <Input label="Địa chỉ thường trú" {...bind('address')} />
          </div>
        </div>

        <fieldset className="mt-6">
          <legend className="text-sm font-bold text-navy">Bảo hiểm y tế</legend>
          <div className="mt-3 grid grid-cols-1 gap-4 md:grid-cols-2">
            <Input label="Số thẻ BHYT" {...bind('insuranceNumber')} />
            <Input label="Nơi ĐKKCB ban đầu" {...bind('insuranceFacility')} />
          </div>
        </fieldset>

        <fieldset className="mt-6">
          <legend className="text-sm font-bold text-navy">Người liên hệ khẩn cấp</legend>
          <div className="mt-3 grid grid-cols-1 gap-4 md:grid-cols-3">
            <Input label="Họ tên người liên hệ" {...bind('emergencyContactName')} />
            <Input label="Mối quan hệ" {...bind('emergencyContactRelationship')} />
            <Input label="SĐT người liên hệ" inputMode="tel" {...bind('emergencyContactPhone')} />
          </div>
        </fieldset>

        <div className="mt-6 flex justify-end gap-3">
          <Button type="button" variant="secondary" aria-label="Hủy bỏ" onClick={onClose}>
            Hủy bỏ
          </Button>
          <Button type="submit" aria-label="Cập nhật hồ sơ" loading={submitting}>
            Cập nhật hồ sơ
          </Button>
        </div>
      </form>
    </Modal>
  );
}
