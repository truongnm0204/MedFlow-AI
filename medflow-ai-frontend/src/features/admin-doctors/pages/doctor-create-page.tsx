import { zodResolver } from '@hookform/resolvers/zod';
import { useEffect, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { Link, useNavigate } from 'react-router-dom';

import { Button } from '@/shared/ui/button';

import { AdminDoctorShell } from '../components/admin-doctor-shell';
import { DoctorCreateAssignment, DoctorCreateIdentity } from '../components/doctor-create-identity';
import {
  DoctorCreateBiography,
  DoctorCreateCredentials,
} from '../components/doctor-create-professional';
import { doctorSchema, type DoctorFormValues } from '../schemas/doctor.schema';
import { useDoctorDemoStore } from '../store/doctor-demo.store';
import type { Doctor, WorkHistoryEntry } from '../types/doctor.types';

const SAMPLE: Doctor = {
  id: 'DOC-0245',
  staffId: 'NV-0089',
  name: 'BS. Phạm Quang Duy',
  email: 'duy.pq@medflow.vn',
  degree: 'Bác sĩ Chuyên khoa I',
  specialty: 'Nội Tiêu hóa & Gan mật - Nội soi can thiệp dải tần hẹp NBI',
  department: 'Khoa Nội Tiêu hóa',
  license: '018492/BYT-CCHN',
  room: 'PK-102 (Phòng Tiêu hóa 2) - Tầng 2, Tòa A',
  phone: '024.3825.8899 - Ext: 204',
  experience: 12,
  scope: 'Khám bệnh, chữa bệnh chuyên khoa Nội Tiêu hóa & Nội soi can thiệp',
  biography:
    'Hơn 12 năm kinh nghiệm trong lĩnh vực Nội Tiêu hóa & Gan Mật. Chuyên sâu chẩn đoán và điều trị viêm loét dạ dày - tá tràng, bệnh lý trào ngược GERD và tầm soát sớm ung thư đường tiêu hóa.',
  active: true,
  history: [
    { year: '2012', description: 'Tốt nghiệp Bác sĩ Đa khoa - Đại học Y Hà Nội' },
    { year: '2017', description: 'Tốt nghiệp Bác sĩ Chuyên khoa I Nội Tiêu hóa - ĐHY Hà Nội' },
    { year: '2018 - 2024', description: 'Bác sĩ điều trị Khoa Tiêu hóa - BV Bạch Mai' },
  ],
};

/** SCR-ADM-DOC-02 — create a local demonstration doctor record. */
export function DoctorCreatePage() {
  const navigate = useNavigate();
  const draft = useDoctorDemoStore((state) => state.draft);
  const doctors = useDoctorDemoStore((state) => state.doctors);
  const addDoctor = useDoctorDemoStore((state) => state.addDoctor);
  const saveDraft = useDoctorDemoStore((state) => state.saveDraft);
  const [history, setHistory] = useState<WorkHistoryEntry[]>(draft?.history ?? SAMPLE.history);
  const [portrait, setPortrait] = useState<string | null>(draft?.portrait ?? null);
  const [portraitError, setPortraitError] = useState('');
  const [message, setMessage] = useState('');
  const {
    register,
    handleSubmit,
    getValues,
    control,
    formState: { errors, isDirty },
  } = useForm<DoctorFormValues>({
    resolver: zodResolver(doctorSchema),
    defaultValues: draft ?? SAMPLE,
  });
  const biography = useWatch({ control, name: 'biography' }) ?? '';
  useEffect(() => {
    if (!isDirty) return;
    const timer = window.setInterval(
      () => saveDraft({ ...getValues(), history, portrait: portrait ?? undefined }),
      30_000,
    );
    return () => window.clearInterval(timer);
  }, [getValues, history, isDirty, portrait, saveDraft]);
  useEffect(() => {
    const handleKeys = (event: KeyboardEvent) => {
      if (event.key === 'Escape') navigate('/admin/doctors');
      if (event.ctrlKey && event.key.toLowerCase() === 's') {
        event.preventDefault();
        document.getElementById('doctor-create-submit')?.click();
      }
    };
    window.addEventListener('keydown', handleKeys);
    return () => window.removeEventListener('keydown', handleKeys);
  }, [navigate]);

  const handlePortrait = (file: File | null) => {
    if (!file) return;
    if (!['image/png', 'image/jpeg'].includes(file.type) || file.size > 2_000_000) {
      setPortraitError('Chọn ảnh JPG/PNG tối đa 2MB.');
      return;
    }
    const reader = new FileReader();
    reader.onload = () => {
      if (typeof reader.result === 'string') {
        setPortrait(reader.result);
        setPortraitError('');
      }
    };
    reader.onerror = () => setPortraitError('Không đọc được ảnh đã chọn.');
    reader.readAsDataURL(file);
  };
  const handleDraft = () => {
    saveDraft({ ...getValues(), history, portrait: portrait ?? undefined });
    setMessage('Đã lưu bản nháp trong phiên hiện tại.');
  };
  const handleCreate = (values: DoctorFormValues) => {
    if (doctors.some((doctor) => doctor.id === values.id || doctor.staffId === values.staffId)) {
      setMessage('Mã bác sĩ hoặc nhân sự đã được sử dụng trong danh sách minh họa.');
      return;
    }
    addDoctor({ ...values, history, portrait: portrait ?? undefined });
    navigate('/admin/doctors');
  };

  return (
    <AdminDoctorShell>
      <form onSubmit={handleSubmit(handleCreate)}>
        <main className="mx-auto max-w-[1280px] px-6 pb-24 pt-7">
          <div className="flex flex-wrap items-center gap-3 text-[11px]">
            <Link
              to="/admin/doctors"
              className="rounded border border-border bg-white px-3 py-2 text-sub hover:text-primary"
            >
              ← Quay lại Danh mục Bác sĩ (SCR-ADM-DOC-01)
            </Link>
            <span className="rounded-full border border-sky-200 bg-sky-50 px-3 py-1.5 font-semibold text-primary">
              ● Tạo mới (Draft Mode)
            </span>
          </div>
          <h1 className="mt-3 border-b border-border pb-5 text-2xl font-bold tracking-tight">
            Thêm mới Hồ sơ Bác sĩ Lâm sàng & Chuyên gia Y tế
          </h1>
          <div className="my-6 flex gap-3 rounded-lg border border-sky-200 bg-sky-50 px-5 py-4 text-xs leading-relaxed text-sky-900">
            <span className="grid h-8 w-8 shrink-0 place-items-center rounded bg-white">
              <img src="/assets/admin-doctors/create-13.svg" alt="" />
            </span>
            <p>
              <b>Lưu ý vận hành Khám Ngoại trú & Đồng bộ dữ liệu Cổng Bệnh nhân:</b>
              <br />
              Biểu mẫu này minh họa liên kết định danh nhân sự (Staff SSO), thông tin chuyên môn và
              buồng khám mặc định. Dữ liệu chỉ tồn tại trong phiên và chưa đồng bộ với Cổng Đặt hẹn
              Bệnh nhân.
            </p>
          </div>
          {message && (
            <p
              role="status"
              className="mb-4 rounded border border-sky-200 bg-sky-50 p-3 text-xs text-sky-800"
            >
              {message}
            </p>
          )}
          <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,1.55fr)_minmax(320px,1fr)]">
            <div className="space-y-6">
              <DoctorCreateIdentity
                register={register}
                errors={errors}
                portrait={portrait}
                onPortraitChange={handlePortrait}
                portraitError={portraitError}
              />
              <DoctorCreateAssignment register={register} errors={errors} />
            </div>
            <div className="space-y-6">
              <DoctorCreateCredentials register={register} errors={errors} />
              <DoctorCreateBiography
                register={register}
                errors={errors}
                history={history}
                onHistoryChange={setHistory}
                biographyLength={biography.length}
              />
            </div>
          </div>
        </main>
        <div className="sticky bottom-0 z-10 border-t border-border bg-white px-6 py-3 shadow-lg">
          <div className="mx-auto flex max-w-[1280px] flex-wrap items-center justify-between gap-3 text-[10px] text-sub">
            <span>
              <b className="text-emerald-600">●</b> Hệ thống tự động lưu nháp sau mỗi 30 giây{' '}
              <span className="ml-2 text-slate-400">• Chuẩn HL7/FHIR Practitioner Resource</span>
            </span>
            <div className="flex items-center gap-2">
              <Button
                type="button"
                variant="secondary"
                size="sm"
                onClick={() => navigate('/admin/doctors')}
              >
                Hủy bỏ [Esc]
              </Button>
              <Button type="button" variant="secondary" size="sm" onClick={handleDraft}>
                Lưu bản nháp
              </Button>
              <Button id="doctor-create-submit" type="submit" size="sm">
                <img src="/assets/admin-doctors/create-03.svg" alt="" />
                Tạo Hồ sơ Bác sĩ [Ctrl+S]
              </Button>
            </div>
          </div>
        </div>
      </form>
    </AdminDoctorShell>
  );
}
