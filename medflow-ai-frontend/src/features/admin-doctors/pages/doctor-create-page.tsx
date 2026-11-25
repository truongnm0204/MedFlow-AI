import { zodResolver } from '@hookform/resolvers/zod';
import { Info, Save } from 'lucide-react';
import { useCallback, useEffect, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { Link, useNavigate } from 'react-router-dom';

import { useAuthStore } from '@/features/auth/store/auth.store';
import { UnsavedChangesModal, useUnsavedChanges } from '@/shared/hooks/use-unsaved-changes';
import { loadAdminDraft, removeAdminDraft, saveAdminDraft } from '@/shared/lib/admin-draft-storage';
import { Button } from '@/shared/ui/button';

import { AdminDoctorShell } from '../components/admin-doctor-shell';
import { DoctorCreateAssignment, DoctorCreateIdentity } from '../components/doctor-create-identity';
import {
  DoctorCreateBiography,
  DoctorCreateCredentials,
} from '../components/doctor-create-professional';
import { useCreateAdminDoctorMutation } from '../hooks/use-admin-doctors';
import { doctorSchema, type DoctorFormValues } from '../schemas/doctor.schema';
import type { Doctor, WorkHistoryEntry } from '../types/doctor.types';

export const DOCTOR_CREATE_DRAFT_STORAGE_KEY = 'medflow.admin-doctor-create.draft';

const EMPTY_DOCTOR_FORM: DoctorFormValues = {
  id: '',
  staffId: '',
  name: '',
  email: '',
  degree: '',
  specialty: '',
  department: '',
  license: '',
  room: '',
  phone: '',
  experience: 1,
  scope: '',
  biography: '',
  active: true,
};

function loadDoctorDraft(userId: number | null): Doctor | null {
  const parsed = loadAdminDraft<Partial<Doctor>>('doctor-create', {
    userId,
    legacyUnauthenticatedKey: DOCTOR_CREATE_DRAFT_STORAGE_KEY,
  });
  if (!parsed) return null;
  return {
    ...EMPTY_DOCTOR_FORM,
    ...parsed,
    history: Array.isArray(parsed.history) ? parsed.history : [],
  };
}

function areHistoriesEqual(left: WorkHistoryEntry[], right: WorkHistoryEntry[]): boolean {
  if (left.length !== right.length) return false;
  for (let i = 0; i < left.length; i += 1) {
    if (left[i].year !== right[i].year || left[i].description !== right[i].description) {
      return false;
    }
  }
  return true;
}

/** SCR-ADM-DOC-02 — create doctor profile via POST /api/v1/admin/doctors. */
export function DoctorCreatePage() {
  const navigate = useNavigate();
  const user = useAuthStore((state) => state.user);
  const createDoctorMutation = useCreateAdminDoctorMutation();

  const [initialDraft] = useState<Doctor | null>(() => loadDoctorDraft(user?.id ?? null));
  const [history, setHistory] = useState<WorkHistoryEntry[]>(initialDraft?.history ?? []);
  const [baselineHistory, setBaselineHistory] = useState<WorkHistoryEntry[]>(
    initialDraft?.history ?? [],
  );
  const [portrait, setPortrait] = useState<string | null>(initialDraft?.portrait ?? null);
  const [portraitError, setPortraitError] = useState('');
  const [message, setMessage] = useState('');

  const {
    register,
    handleSubmit,
    getValues,
    reset,
    control,
    formState: { errors, isDirty },
  } = useForm<DoctorFormValues>({
    resolver: zodResolver(doctorSchema),
    defaultValues: initialDraft ?? EMPTY_DOCTOR_FORM,
  });

  // Portrait images are intentionally excluded from sessionStorage drafts,
  // so an unsubmitted portrait keeps the form dirty until final submit.
  const isFormDirty = isDirty || !areHistoriesEqual(history, baselineHistory) || portrait !== null;
  const { isBlocked, confirmNavigation, cancelNavigation, allowNavigation, guardAction } =
    useUnsavedChanges(isFormDirty);

  const biography = useWatch({ control, name: 'biography' }) ?? '';

  const persistDraft = useCallback(
    (values: DoctorFormValues, nextHistory: WorkHistoryEntry[]) => {
      const payload: Doctor = {
        ...values,
        history: nextHistory.map((entry) => ({ ...entry })),
      };
      return saveAdminDraft('doctor-create', payload, {
        userId: user?.id ?? null,
        legacyUnauthenticatedKey: DOCTOR_CREATE_DRAFT_STORAGE_KEY,
      });
    },
    [user?.id],
  );

  useEffect(() => {
    if (!isFormDirty) return;
    const timer = window.setInterval(() => {
      persistDraft(getValues(), history);
    }, 30_000);
    return () => window.clearInterval(timer);
  }, [getValues, history, isFormDirty, persistDraft]);

  useEffect(() => {
    const handleKeys = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        guardAction(() => navigate('/admin/doctors'));
      }
      if (event.ctrlKey && event.key.toLowerCase() === 's') {
        event.preventDefault();
        document.getElementById('doctor-create-submit')?.click();
      }
    };
    window.addEventListener('keydown', handleKeys);
    return () => window.removeEventListener('keydown', handleKeys);
  }, [guardAction, navigate]);

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
    const snapshotValues: DoctorFormValues = { ...getValues() };
    const snapshotHistory = history.map((entry) => ({ ...entry }));
    const saved = persistDraft(snapshotValues, snapshotHistory);
    if (!saved.ok) {
      setMessage(saved.error);
      return;
    }
    reset(snapshotValues, { keepValues: true });
    setBaselineHistory(snapshotHistory);
    setMessage('Đã lưu bản nháp trong phiên hiện tại.');
  };

  const handleCreate = async (values: DoctorFormValues) => {
    setMessage('');
    const rawSnapshot = structuredClone(getValues());
    const submittedHistory = history.map((entry) => ({ ...entry }));
    const submittedPortrait = portrait;
    try {
      await createDoctorMutation.mutateAsync({
        ...values,
        history: submittedHistory,
        portrait: submittedPortrait ?? undefined,
      });
      const editedDuringSubmit =
        JSON.stringify(getValues()) !== JSON.stringify(rawSnapshot) ||
        !areHistoriesEqual(history, submittedHistory) ||
        portrait !== submittedPortrait;
      removeAdminDraft('doctor-create', {
        userId: user?.id ?? null,
        legacyUnauthenticatedKey: DOCTOR_CREATE_DRAFT_STORAGE_KEY,
      });
      reset(rawSnapshot, { keepValues: true });
      setBaselineHistory(submittedHistory);
      if (!editedDuringSubmit) {
        allowNavigation();
        navigate('/admin/doctors');
      } else {
        guardAction(() => navigate('/admin/doctors'));
      }
    } catch (err) {
      setMessage(err instanceof Error ? err.message : 'Không thể tạo hồ sơ bác sĩ.');
    }
  };

  return (
    <AdminDoctorShell>
      <form onSubmit={handleSubmit(handleCreate)} noValidate>
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
              <Info className="h-4 w-4 text-primary" aria-hidden="true" />
            </span>
            <p>
              <b>Lưu ý vận hành Khám Ngoại trú & Đồng bộ dữ liệu Cổng Bệnh nhân:</b>
              <br />
              Hồ sơ bác sĩ cần liên kết với mã nhân sự (Staff Code) hợp lệ tại cơ sở y tế để đồng bộ
              danh mục bác sĩ và lịch khám trực tuyến.
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
                onClick={() => guardAction(() => navigate('/admin/doctors'))}
              >
                Hủy bỏ [Esc]
              </Button>
              <Button type="button" variant="secondary" size="sm" onClick={handleDraft}>
                Lưu bản nháp
              </Button>
              <Button
                id="doctor-create-submit"
                type="submit"
                size="sm"
                disabled={createDoctorMutation.isPending}
              >
                <Save className="h-4 w-4" aria-hidden="true" />
                Tạo Hồ sơ Bác sĩ [Ctrl+S]
              </Button>
            </div>
          </div>
        </div>
      </form>

      <UnsavedChangesModal open={isBlocked} onStay={cancelNavigation} onLeave={confirmNavigation} />
    </AdminDoctorShell>
  );
}
