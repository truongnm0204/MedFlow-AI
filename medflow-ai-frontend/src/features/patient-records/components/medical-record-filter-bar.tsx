import { useState, type FormEvent } from 'react';
import { AlertCircle, Filter, RotateCcw, Search } from 'lucide-react';

import {
  medicalRecordFilterSchema,
  type MedicalRecordFilterFormValues,
} from '../schemas/medical-record-filter.schema';
import type {
  PatientEmrFilterOption,
  PatientEmrScopeMember,
  PatientMedicalRecordFilterParams,
} from '../types/patient-records.types';

export interface MedicalRecordFilterBarProps {
  members: PatientEmrScopeMember[];
  hospitals: PatientEmrFilterOption[];
  specialties: PatientEmrFilterOption[];
  initialValues: MedicalRecordFilterFormValues;
  onApply: (next: PatientMedicalRecordFilterParams) => void;
  onReset: () => void;
}

export function MedicalRecordFilterBar({
  members,
  hospitals,
  specialties,
  initialValues,
  onApply,
  onReset,
}: MedicalRecordFilterBarProps) {
  const [values, setValues] = useState<MedicalRecordFilterFormValues>(initialValues);
  const [validationError, setValidationError] = useState<string | null>(null);

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const parsed = medicalRecordFilterSchema.safeParse(values);
    if (!parsed.success) {
      setValidationError(parsed.error.issues[0]?.message ?? 'Bộ lọc không hợp lệ.');
      return;
    }

    setValidationError(null);
    const data = parsed.data;
    onApply({
      patientId: data.patientId ? Number(data.patientId) : undefined,
      fromDate: data.fromDate || undefined,
      toDate: data.toDate || undefined,
      hospitalId: data.hospitalId ? Number(data.hospitalId) : undefined,
      specialtyCode: data.specialtyCode || undefined,
      keyword: data.keyword.trim() || undefined,
      page: 1,
    });
  };

  const handleClear = () => {
    setValues({
      patientId: '',
      fromDate: '',
      toDate: '',
      hospitalId: '',
      specialtyCode: '',
      keyword: '',
    });
    setValidationError(null);
    onReset();
  };

  return (
    <form
      onSubmit={handleSubmit}
      aria-label="Bộ lọc hồ sơ bệnh án"
      className="rounded-2xl border border-border bg-white p-5 shadow-xs"
    >
      <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-6">
        <div>
          <label
            htmlFor="emr-filter-member"
            className="mb-1.5 block text-xs font-semibold text-navy"
          >
            Thành viên gia đình
          </label>
          <select
            id="emr-filter-member"
            value={values.patientId}
            onChange={(e) => setValues((prev) => ({ ...prev, patientId: e.target.value }))}
            className="w-full rounded-xl border border-border bg-canvas px-3 py-2 text-xs text-ink focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
          >
            <option value="">Tất cả hồ sơ được phép</option>
            {members.map((member) => (
              <option key={member.patientId} value={String(member.patientId)}>
                {member.fullName} ({member.relationshipLabel})
              </option>
            ))}
          </select>
        </div>

        <div>
          <label htmlFor="emr-filter-from" className="mb-1.5 block text-xs font-semibold text-navy">
            Từ ngày khám
          </label>
          <input
            id="emr-filter-from"
            type="date"
            value={values.fromDate}
            onChange={(e) => setValues((prev) => ({ ...prev, fromDate: e.target.value }))}
            className="w-full rounded-xl border border-border bg-canvas px-3 py-2 text-xs text-ink focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
          />
        </div>

        <div>
          <label htmlFor="emr-filter-to" className="mb-1.5 block text-xs font-semibold text-navy">
            Đến ngày khám
          </label>
          <input
            id="emr-filter-to"
            type="date"
            value={values.toDate}
            onChange={(e) => setValues((prev) => ({ ...prev, toDate: e.target.value }))}
            className="w-full rounded-xl border border-border bg-canvas px-3 py-2 text-xs text-ink focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
          />
        </div>

        <div>
          <label
            htmlFor="emr-filter-hospital"
            className="mb-1.5 block text-xs font-semibold text-navy"
          >
            Cơ sở y tế
          </label>
          <select
            id="emr-filter-hospital"
            value={values.hospitalId}
            onChange={(e) => setValues((prev) => ({ ...prev, hospitalId: e.target.value }))}
            className="w-full rounded-xl border border-border bg-canvas px-3 py-2 text-xs text-ink focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
          >
            <option value="">Tất cả cơ sở</option>
            {hospitals.map((hospital) => (
              <option key={hospital.id} value={String(hospital.id)}>
                {hospital.name}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label
            htmlFor="emr-filter-specialty"
            className="mb-1.5 block text-xs font-semibold text-navy"
          >
            Chuyên khoa
          </label>
          <select
            id="emr-filter-specialty"
            value={values.specialtyCode}
            onChange={(e) => setValues((prev) => ({ ...prev, specialtyCode: e.target.value }))}
            className="w-full rounded-xl border border-border bg-canvas px-3 py-2 text-xs text-ink focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
          >
            <option value="">Tất cả chuyên khoa</option>
            {specialties.map((specialty) => (
              <option key={specialty.id} value={specialty.code ?? ''}>
                {specialty.name}
              </option>
            ))}
          </select>
        </div>

        <div>
          <label
            htmlFor="emr-filter-keyword"
            className="mb-1.5 block text-xs font-semibold text-navy"
          >
            Tìm kiếm nhanh
          </label>
          <div className="relative">
            <Search
              className="pointer-events-none absolute top-1/2 left-3 h-3.5 w-3.5 -translate-y-1/2 text-sub"
              aria-hidden="true"
            />
            <input
              id="emr-filter-keyword"
              type="text"
              placeholder="Mã lượt khám, ICD-10, bác sĩ..."
              value={values.keyword}
              onChange={(e) => setValues((prev) => ({ ...prev, keyword: e.target.value }))}
              className="w-full rounded-xl border border-border bg-canvas py-2 pr-3 pl-8 text-xs text-ink placeholder:text-slate-400 focus-visible:ring-2 focus-visible:ring-primary focus-visible:outline-none"
            />
          </div>
        </div>
      </div>

      {validationError && (
        <div
          role="alert"
          className="mt-3 flex items-center gap-2 rounded-xl border border-rose-200 bg-rose-50 px-3.5 py-2.5 text-xs font-medium text-rose-700"
        >
          <AlertCircle className="h-4 w-4 shrink-0" aria-hidden="true" />
          <span>{validationError}</span>
        </div>
      )}

      <div className="mt-4 flex flex-wrap items-center justify-end gap-2.5 border-t border-border pt-3.5">
        <button
          type="button"
          onClick={handleClear}
          className="inline-flex items-center gap-1.5 rounded-xl border border-border bg-white px-3.5 py-2 text-xs font-semibold text-sub transition-colors hover:bg-canvas hover:text-navy focus-visible:ring-2 focus-visible:ring-primary"
        >
          <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
          <span>Đặt lại</span>
        </button>
        <button
          type="submit"
          className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-4 py-2 text-xs font-semibold text-white shadow-xs transition-colors hover:bg-primary-dark focus-visible:ring-2 focus-visible:ring-primary"
        >
          <Filter className="h-3.5 w-3.5" aria-hidden="true" />
          <span>Áp dụng bộ lọc</span>
        </button>
      </div>
    </form>
  );
}
