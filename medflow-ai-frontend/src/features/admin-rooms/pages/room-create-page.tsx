import { zodResolver } from '@hookform/resolvers/zod';
import { Plus } from 'lucide-react';
import { useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { Link, useNavigate } from 'react-router-dom';

import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';

import {
  DEFAULT_ROOM_FORM_VALUES,
  ROOM_CAPACITY_OPTIONS,
  ROOM_DEPARTMENTS,
  roomCreateSchema,
  type RoomCreateFormValues,
} from '../schemas/room-create.schema';
import type { RoomStatus, RoomType } from '../types/room-create.types';

const ROOM_TYPE_LABELS: Record<RoomType, string> = {
  OUTPATIENT: 'Khám ngoại trú',
  PROCEDURAL: 'Kỹ thuật',
};

const ROOM_STATUS_LABELS: Record<RoomStatus, string> = {
  ACTIVE: 'Đang hoạt động',
  MAINTENANCE: 'Bảo trì',
  INACTIVE: 'Tạm ngừng',
};

const DRAFT_KEY = 'medflow.room-create.draft';

const inputClass =
  'mt-1 w-full rounded-lg border border-border bg-canvas px-3 py-2 text-sm text-ink outline-none focus:border-primary';
const labelClass = 'block text-sm font-semibold text-navy';
const errorClass = 'mt-1 text-xs text-red-600';

function loadDraft(): RoomCreateFormValues {
  try {
    const raw = window.sessionStorage.getItem(DRAFT_KEY);
    if (raw) {
      const parsed = roomCreateSchema.partial().safeParse(JSON.parse(raw));
      if (parsed.success) return { ...DEFAULT_ROOM_FORM_VALUES, ...parsed.data };
    }
  } catch {
    // ignore corrupt draft
  }
  return { ...DEFAULT_ROOM_FORM_VALUES };
}

export function RoomCreatePage() {
  const navigate = useNavigate();
  const [message, setMessage] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    setValue,
    getValues,
    control,
    formState: { errors, isSubmitting },
  } = useForm<RoomCreateFormValues>({
    resolver: zodResolver(roomCreateSchema),
    defaultValues: loadDraft(),
  });

  const values = useWatch({ control });

  const handleCreate = (data: RoomCreateFormValues) => {
    window.sessionStorage.removeItem(DRAFT_KEY);
    setMessage(`Đã khai báo buồng khám ${data.code} - ${data.name}.`);
    navigate('/admin/rooms');
  };

  const handleSaveDraft = () => {
    window.sessionStorage.setItem(DRAFT_KEY, JSON.stringify(getValues()));
    setMessage('Đã lưu bản nháp buồng khám.');
  };

  const setCapacity = (capacity: number) =>
    setValue('capacity', capacity, { shouldDirty: true, shouldValidate: true });

  const previewRows: [string, string][] = [
    ['Mã phòng', values.code || '—'],
    ['Tên phòng', values.name || '—'],
    ['Loại phòng', values.type ? ROOM_TYPE_LABELS[values.type] : '—'],
    ['Khoa phụ trách', values.department || '—'],
    ['Vị trí phòng', values.location || '—'],
    ['Sức chứa', values.capacity ? `${values.capacity} người / slot` : '—'],
    ['Trạng thái', values.status ? ROOM_STATUS_LABELS[values.status] : '—'],
  ];

  return (
    <AdminPortalShell
      activeTab="Buồng khám & Kỹ thuật"
      showTabs={false}
      breadcrumbs={[
        { label: 'Danh mục Buồng khám (SCR-ADM-ROOM-01)', href: '/admin/rooms' },
        { label: 'Khai báo buồng khám' },
      ]}
    >
      <form onSubmit={handleSubmit(handleCreate)} noValidate>
        <main className="mx-auto max-w-[1720px] px-6 pb-28 pt-6">
          <Link to="/admin/rooms" className="text-xs font-medium text-sub hover:text-navy">
            ← Danh mục Buồng khám (SCR-ADM-ROOM-01)
          </Link>
          <h1 className="mt-3 text-2xl font-bold text-navy">Khai báo buồng khám</h1>
          <p className="mt-1 text-sm text-sub">
            Khởi tạo cơ sở, khoa, vị trí, sức chứa và trạng thái phòng
          </p>

          {message ? (
            <div
              role="status"
              className="mt-4 rounded-lg border border-border bg-white p-3 text-sm text-ink"
            >
              {message}
            </div>
          ) : null}

          <div className="mt-6 grid gap-6 lg:grid-cols-[minmax(0,1fr)_380px]">
            <div className="space-y-6">
              <section className="rounded-xl border border-border bg-white p-6">
                <h2 className="text-xs font-bold uppercase tracking-wide text-navy">
                  1. THÔNG TIN CHÍNH & PHẠM VI
                </h2>
                <div className="mt-4 grid gap-4 md:grid-cols-2">
                  <div>
                    <label htmlFor="room-code" className={labelClass}>
                      Mã phòng *
                    </label>
                    <input
                      id="room-code"
                      placeholder="RM-202"
                      className={`${inputClass} font-mono`}
                      {...register('code')}
                    />
                    {errors.code ? <p className={errorClass}>{errors.code.message}</p> : null}
                  </div>
                  <div>
                    <label htmlFor="room-name" className={labelClass}>
                      Tên phòng *
                    </label>
                    <input
                      id="room-name"
                      placeholder="Phòng khám Nội tiết 2"
                      className={inputClass}
                      {...register('name')}
                    />
                    {errors.name ? <p className={errorClass}>{errors.name.message}</p> : null}
                  </div>
                </div>

                <fieldset className="mt-4">
                  <legend className={labelClass}>Loại phòng *</legend>
                  <div className="mt-2 flex flex-wrap gap-4">
                    {(Object.keys(ROOM_TYPE_LABELS) as RoomType[]).map((type) => (
                      <label key={type} className="flex items-center gap-2 text-sm text-ink">
                        <input type="radio" value={type} {...register('type')} />
                        <span>
                          {type} ({ROOM_TYPE_LABELS[type]})
                        </span>
                      </label>
                    ))}
                  </div>
                </fieldset>

                <div className="mt-4">
                  <label htmlFor="room-department" className={labelClass}>
                    Khoa phụ trách *
                  </label>
                  <select id="room-department" className={inputClass} {...register('department')}>
                    <option value="">Chọn khoa phụ trách</option>
                    {ROOM_DEPARTMENTS.map((department) => (
                      <option key={department} value={department}>
                        {department}
                      </option>
                    ))}
                  </select>
                  {errors.department ? (
                    <p className={errorClass}>{errors.department.message}</p>
                  ) : null}
                </div>

                <div className="mt-4">
                  <label htmlFor="room-location" className={labelClass}>
                    Vị trí phòng *
                  </label>
                  <input
                    id="room-location"
                    placeholder="Nhà A - Tầng 2 - Khu khám ngoại trú"
                    className={inputClass}
                    {...register('location')}
                  />
                  {errors.location ? <p className={errorClass}>{errors.location.message}</p> : null}
                </div>
              </section>

              <section className="rounded-xl border border-border bg-white p-6">
                <h2 className="text-xs font-bold uppercase tracking-wide text-navy">
                  2. CẤU HÌNH VẬN HÀNH
                </h2>
                <fieldset className="mt-4">
                  <legend className={labelClass}>Sức chứa phòng *</legend>
                  <div className="mt-2 flex flex-wrap gap-2">
                    {ROOM_CAPACITY_OPTIONS.map((capacity) => {
                      const selected = values.capacity === capacity;
                      return (
                        <button
                          key={capacity}
                          type="button"
                          aria-label={`${capacity} người / slot`}
                          aria-pressed={selected}
                          onClick={() => setCapacity(capacity)}
                          className={`rounded-full border px-4 py-1.5 text-sm font-medium ${
                            selected
                              ? 'border-primary bg-primary text-white'
                              : 'border-border bg-white text-ink hover:border-primary'
                          }`}
                        >
                          {capacity} người / slot
                        </button>
                      );
                    })}
                  </div>
                  {errors.capacity ? <p className={errorClass}>{errors.capacity.message}</p> : null}
                </fieldset>

                <fieldset className="mt-4">
                  <legend className={labelClass}>Trạng thái phòng *</legend>
                  <div className="mt-2 flex flex-wrap gap-4">
                    {(Object.keys(ROOM_STATUS_LABELS) as RoomStatus[]).map((status) => (
                      <label key={status} className="flex items-center gap-2 text-sm text-ink">
                        <input type="radio" value={status} {...register('status')} />
                        <span>
                          {status} - {ROOM_STATUS_LABELS[status]}
                        </span>
                      </label>
                    ))}
                  </div>
                </fieldset>
              </section>
            </div>

            <aside
              aria-label="Xem trước buồng khám"
              className="h-fit rounded-xl border border-border bg-white p-6"
            >
              <h2 className="text-xs font-bold uppercase tracking-wide text-navy">
                Xem trước buồng khám
              </h2>
              <dl className="mt-4 space-y-3 text-sm">
                {previewRows.map(([label, value]) => (
                  <div key={label} className="flex justify-between gap-4">
                    <dt className="text-sub">{label}</dt>
                    <dd
                      data-testid={`preview-${label}`}
                      className="text-right font-medium text-ink"
                    >
                      {value}
                    </dd>
                  </div>
                ))}
              </dl>
            </aside>
          </div>
        </main>

        <div className="fixed inset-x-0 bottom-0 z-20 flex justify-end gap-3 border-t border-border bg-white px-6 py-4">
          <button
            type="button"
            aria-label="Hủy bỏ"
            onClick={() => navigate('/admin/rooms')}
            className="rounded-lg border border-border px-4 py-2 text-sm font-semibold text-ink"
          >
            Hủy bỏ
          </button>
          <button
            type="button"
            aria-label="Lưu bản nháp"
            onClick={handleSaveDraft}
            className="rounded-lg border border-border px-4 py-2 text-sm font-semibold text-navy"
          >
            Lưu bản nháp
          </button>
          <button
            type="submit"
            aria-label="Khai báo buồng khám"
            disabled={isSubmitting}
            className="flex items-center gap-2 rounded-lg bg-primary px-4 py-2 text-sm font-semibold text-white hover:bg-primary-dark"
          >
            <Plus className="h-4 w-4" aria-hidden="true" />
            Khai báo buồng khám
          </button>
        </div>
      </form>
    </AdminPortalShell>
  );
}
