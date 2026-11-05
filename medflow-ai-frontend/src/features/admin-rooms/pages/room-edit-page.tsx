import { zodResolver } from '@hookform/resolvers/zod';
import { useForm, useWatch } from 'react-hook-form';
import { Link, useNavigate, useParams } from 'react-router-dom';

import { AdminPortalShell } from '@/shared/components/layout/admin-portal-shell';

import { findRoomById, ROOM_DEPARTMENTS } from '../data/room-edit-mock';
import { roomUpdateSchema, type RoomUpdateFormValues } from '../schemas/room-update.schema';
import type { RoomRecord, RoomStatus, RoomType } from '../types/room-update.types';

const ROOM_TYPES: { value: RoomType; label: string }[] = [
  { value: 'OUTPATIENT', label: 'OUTPATIENT' },
  { value: 'PROCEDURAL', label: 'PROCEDURAL' },
];

const STATUS_LABELS: Record<RoomStatus, string> = {
  ACTIVE: 'Đang hoạt động',
  MAINTENANCE: 'Bảo trì',
  INACTIVE: 'Tạm ngừng',
};

const CAPACITIES = [1, 2, 3, 4] as const;

const inputClass =
  'w-full rounded-lg border border-border bg-white px-3 py-2 text-sm text-ink outline-none focus:border-primary';

function RoomEditForm({ room }: { room: RoomRecord }) {
  const navigate = useNavigate();
  const {
    register,
    handleSubmit,
    setValue,
    control,
    formState: { errors },
  } = useForm<RoomUpdateFormValues>({
    resolver: zodResolver(roomUpdateSchema),
    defaultValues: {
      name: room.name,
      type: room.type,
      department: room.department,
      location: room.location,
      capacity: room.capacity,
      status: room.status,
    },
  });

  const values = useWatch({ control });

  const onSubmit = () => {
    navigate('/admin/rooms');
  };

  return (
    <AdminPortalShell
      activeTab="Buồng khám & Kỹ thuật"
      showTabs={false}
      breadcrumbs={[
        { label: 'Cấu hình Cơ sở' },
        { label: 'Danh mục Buồng khám (SCR-ADM-ROOM-01)', href: '/admin/rooms' },
        { label: 'Cấu hình buồng khám' },
      ]}
    >
      <form onSubmit={handleSubmit(onSubmit)} noValidate>
        <main className="mx-auto max-w-[1720px] px-6 pb-28 pt-6">
          <Link
            to="/admin/rooms"
            className="rounded-lg border border-border bg-white px-3 py-1.5 text-xs text-sub hover:text-primary"
          >
            ← Danh mục Buồng khám (SCR-ADM-ROOM-01)
          </Link>
          <h1 className="mt-4 text-2xl font-bold text-navy">Cấu hình {values.name || room.name}</h1>
          <p className="mt-1 text-sm text-sub">
            Khởi tạo cơ sở, khoa, vị trí, sức chứa và trạng thái phòng
          </p>

          <div className="mt-6 grid gap-6 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
            <div className="space-y-6 rounded-xl border border-border bg-white p-6">
              <section className="space-y-4">
                <h2 className="text-xs font-bold uppercase tracking-wide text-navy">
                  1. THÔNG TIN CHÍNH & PHẠM VI
                </h2>
                <div>
                  <label htmlFor="room-code" className="mb-1 block text-xs font-semibold text-ink">
                    Mã phòng *
                  </label>
                  <input
                    id="room-code"
                    value={room.code}
                    readOnly
                    className={`${inputClass} bg-canvas font-mono`}
                  />
                </div>
                <div>
                  <label htmlFor="room-name" className="mb-1 block text-xs font-semibold text-ink">
                    Tên phòng *
                  </label>
                  <input id="room-name" {...register('name')} className={inputClass} />
                  {errors.name && (
                    <p role="alert" className="mt-1 text-xs text-red-600">
                      {errors.name.message}
                    </p>
                  )}
                </div>
                <fieldset>
                  <legend className="mb-1 text-xs font-semibold text-ink">Loại phòng *</legend>
                  <div className="flex gap-2" role="radiogroup" aria-label="Loại phòng">
                    {ROOM_TYPES.map((t) => (
                      <button
                        key={t.value}
                        type="button"
                        role="radio"
                        aria-checked={values.type === t.value}
                        aria-label={`Loại phòng ${t.label}`}
                        onClick={() => setValue('type', t.value, { shouldDirty: true })}
                        className={`rounded-lg border px-4 py-2 text-xs font-semibold ${
                          values.type === t.value
                            ? 'border-primary bg-primary text-white'
                            : 'border-border bg-white text-sub'
                        }`}
                      >
                        {t.label}
                      </button>
                    ))}
                  </div>
                </fieldset>
                <div>
                  <label htmlFor="room-dept" className="mb-1 block text-xs font-semibold text-ink">
                    Khoa phụ trách *
                  </label>
                  <select id="room-dept" {...register('department')} className={inputClass}>
                    <option value="">-- Chọn khoa --</option>
                    {ROOM_DEPARTMENTS.map((d) => (
                      <option key={d} value={d}>
                        {d}
                      </option>
                    ))}
                  </select>
                  {errors.department && (
                    <p role="alert" className="mt-1 text-xs text-red-600">
                      {errors.department.message}
                    </p>
                  )}
                </div>
                <div>
                  <label htmlFor="room-loc" className="mb-1 block text-xs font-semibold text-ink">
                    Vị trí phòng *
                  </label>
                  <input id="room-loc" {...register('location')} className={inputClass} />
                  {errors.location && (
                    <p role="alert" className="mt-1 text-xs text-red-600">
                      {errors.location.message}
                    </p>
                  )}
                </div>
              </section>

              <section className="space-y-4">
                <h2 className="text-xs font-bold uppercase tracking-wide text-navy">
                  2. CẤU HÌNH VẬN HÀNH
                </h2>
                <fieldset>
                  <legend className="mb-1 text-xs font-semibold text-ink">Sức chứa phòng *</legend>
                  <div className="flex gap-2" role="radiogroup" aria-label="Sức chứa phòng">
                    {CAPACITIES.map((c) => (
                      <button
                        key={c}
                        type="button"
                        role="radio"
                        aria-checked={values.capacity === c}
                        aria-label={`${c} người`}
                        onClick={() => setValue('capacity', c, { shouldDirty: true })}
                        className={`rounded-full border px-4 py-1.5 text-xs font-semibold ${
                          values.capacity === c
                            ? 'border-primary bg-primary text-white'
                            : 'border-border bg-white text-sub'
                        }`}
                      >
                        {c} người
                      </button>
                    ))}
                  </div>
                </fieldset>
                <div>
                  <label
                    htmlFor="room-status"
                    className="mb-1 block text-xs font-semibold text-ink"
                  >
                    Trạng thái phòng *
                  </label>
                  <select id="room-status" {...register('status')} className={inputClass}>
                    {(Object.keys(STATUS_LABELS) as RoomStatus[]).map((s) => (
                      <option key={s} value={s}>
                        {s} - {STATUS_LABELS[s]}
                      </option>
                    ))}
                  </select>
                </div>
              </section>
            </div>

            <aside
              aria-label="Xem trước buồng khám"
              data-testid="room-live-preview"
              className="h-fit space-y-3 rounded-xl border border-border bg-white p-6"
            >
              <h2 className="text-xs font-bold uppercase tracking-wide text-navy">
                Xem trước trực tiếp
              </h2>
              <p className="font-mono text-xs text-sub">{room.code}</p>
              <p className="text-lg font-bold text-navy">{values.name}</p>
              <dl className="space-y-1 text-sm text-ink">
                <div>
                  <dt className="inline text-sub">Loại: </dt>
                  <dd className="inline">{values.type}</dd>
                </div>
                <div>
                  <dt className="inline text-sub">Khoa: </dt>
                  <dd className="inline">{values.department}</dd>
                </div>
                <div>
                  <dt className="inline text-sub">Vị trí: </dt>
                  <dd className="inline">{values.location}</dd>
                </div>
                <div>
                  <dt className="inline text-sub">Sức chứa: </dt>
                  <dd className="inline">{values.capacity} người</dd>
                </div>
                <div>
                  <dt className="inline text-sub">Trạng thái: </dt>
                  <dd className="inline">{values.status ? STATUS_LABELS[values.status] : ''}</dd>
                </div>
              </dl>
            </aside>
          </div>
        </main>

        <div className="fixed inset-x-0 bottom-0 z-30 border-t border-border bg-white px-6 py-3">
          <div className="mx-auto flex max-w-[1720px] justify-end gap-3">
            <button
              type="button"
              aria-label="Hủy bỏ"
              onClick={() => navigate('/admin/rooms')}
              className="rounded-lg border border-border bg-white px-4 py-2 text-sm text-sub"
            >
              Hủy bỏ
            </button>
            <button
              type="submit"
              aria-label="Lưu thay đổi"
              className="rounded-lg bg-primary px-4 py-2 text-sm font-semibold text-white hover:bg-primary-dark"
            >
              Lưu thay đổi
            </button>
          </div>
        </div>
      </form>
    </AdminPortalShell>
  );
}

export function RoomEditPage() {
  const { id } = useParams<{ id: string }>();
  const room = findRoomById(id);

  if (!room) {
    return (
      <AdminPortalShell activeTab="Buồng khám & Kỹ thuật" showTabs={false}>
        <main className="mx-auto max-w-[1720px] px-6 py-10">
          <p role="alert" className="text-sm text-ink">
            Không tìm thấy buồng khám
          </p>
          <Link to="/admin/rooms" className="text-sm text-primary">
            ← Danh mục Buồng khám (SCR-ADM-ROOM-01)
          </Link>
        </main>
      </AdminPortalShell>
    );
  }

  return <RoomEditForm key={room.id} room={room} />;
}
