import type { FieldErrors, UseFormRegister } from 'react-hook-form';

import { DEPARTMENTS } from '../data/doctor-demo-data';
import type { DoctorFormValues } from '../schemas/doctor.schema';
import { DoctorCard, DoctorField, doctorControlClass } from './doctor-form-primitives';

interface Props {
  register: UseFormRegister<DoctorFormValues>;
  errors: FieldErrors<DoctorFormValues>;
  portrait: string | null;
  onPortraitChange: (file: File | null) => void;
  portraitError: string;
}

export function DoctorCreateIdentity({
  register,
  errors,
  portrait,
  onPortraitChange,
  portraitError,
}: Props) {
  return (
    <DoctorCard
      title="1. ĐỊNH DANH & LIÊN KẾT TÀI KHOẢN NHÂN SỰ"
      note="Bắt buộc theo chuẩn ISO/HL7"
    >
      <div className="space-y-5">
        <DoctorField
          label="Chọn Nhân viên Y tế từ Danh mục Staff"
          required
          hint="Danh sách nhân sự minh họa; chưa kết nối hồ sơ Staff SSO."
          error={errors.staffId?.message}
        >
          <select className={doctorControlClass} {...register('staffId')}>
            <option value="">Chọn nhân sự</option>
            <option value="NV-0089">
              NV-0089 • BS. Phạm Quang Duy (Khoa Khám bệnh - Chưa liên kết bác sĩ)
            </option>
          </select>
        </DoctorField>
        <div className="grid gap-5 sm:grid-cols-[1fr_180px]">
          <div className="space-y-4">
            <DoctorField
              label="Mã Bác sĩ HIS Định danh"
              required
              hint="Nhập mã minh họa theo dạng DOC-0000."
              error={errors.id?.message}
            >
              <input className={doctorControlClass} {...register('id')} />
            </DoctorField>
            <div className="rounded-md border border-border bg-canvas p-3 text-[10px] text-sub">
              <div className="flex justify-between">
                <span>Trạng thái nhân sự:</span>
                <b className="text-emerald-700">Minh họa (Toàn thời gian)</b>
              </div>
              <div className="mt-1 flex justify-between">
                <span>Tài khoản SSO Portal:</span>
                <b>Chưa kết nối</b>
              </div>
            </div>
          </div>
          <label className="flex min-h-32 cursor-pointer flex-col items-center justify-center rounded-lg border border-dashed border-sky-300 bg-sky-50/40 p-3 text-center text-[10px] text-sub">
            {portrait ? (
              <img
                src={portrait}
                alt="Ảnh chân dung đã chọn"
                className="h-16 w-16 rounded-full object-cover"
              />
            ) : (
              <span className="grid h-10 w-10 place-items-center rounded-full border border-sky-200 bg-white">
                <img src="/assets/admin-doctors/create-09.svg" alt="" />
              </span>
            )}
            <b className="mt-2 text-primary">Tải ảnh áo Blouse trắng</b>
            <span>JPG, PNG tối đa 2MB</span>
            <span className="text-slate-400">Khuyến nghị 400×400 px</span>
            <input
              className="sr-only"
              type="file"
              accept="image/png,image/jpeg"
              onChange={(event) => onPortraitChange(event.target.files?.[0] ?? null)}
            />
            {portraitError && <span className="mt-1 text-rose-600">{portraitError}</span>}
          </label>
        </div>
      </div>
    </DoctorCard>
  );
}

export function DoctorCreateAssignment({ register, errors }: Pick<Props, 'register' | 'errors'>) {
  return (
    <DoctorCard title="2. PHÂN CÔNG CÔNG TÁC & TIẾP NHẬN BUỒNG KHÁM" note="Khu vực Khám Ngoại trú">
      <div className="grid gap-5 sm:grid-cols-2">
        <DoctorField
          label="Khoa công tác trực thuộc"
          required
          hint="Xác định cây phân cấp khoa phòng và luồng thanh toán."
          error={errors.department?.message}
        >
          <select className={doctorControlClass} {...register('department')}>
            <option value="">Chọn khoa</option>
            {DEPARTMENTS.map((item) => (
              <option key={item}>{item}</option>
            ))}
          </select>
        </DoctorField>
        <DoctorField
          label="Buồng khám mặc định"
          required
          hint="Mặc định hiển thị khi y tá xếp số tự động."
          error={errors.room?.message}
        >
          <select className={doctorControlClass} {...register('room')}>
            <option value="">Chọn buồng khám</option>
            <option>PK-102 (Phòng Tiêu hóa 2) - Tầng 2, Tòa A</option>
            <option>PK-101 (Phòng Tiêu hóa 1)</option>
            <option>PK-108 (Phòng Tim mạch)</option>
          </select>
        </DoctorField>
        <DoctorField label="Điện thoại bàn / Nội bộ Buồng khám">
          <input className={doctorControlClass} {...register('phone')} />
        </DoctorField>
        <DoctorField label="Email tiếp nhận ca khám (Auto SSO)" error={errors.email?.message}>
          <input type="email" className={doctorControlClass} {...register('email')} />
        </DoctorField>
      </div>
      <label className="mt-5 flex cursor-pointer items-center justify-between gap-4 rounded-md border border-border bg-canvas p-3 text-[11px]">
        <span>
          <b>Kích hoạt nhận lịch khám ngay (Active)</b>
          <small className="mt-1 block text-sub">
            Cho phép Cổng Bệnh nhân và Kiosk phân bổ slot khám theo lịch trực buồng.
          </small>
        </span>
        <input type="checkbox" className="accent-emerald-500" {...register('active')} />
      </label>
    </DoctorCard>
  );
}
