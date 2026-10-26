import type { FieldErrors, UseFormRegister } from 'react-hook-form';

import { DEGREES } from '../data/doctor-demo-data';
import type { DoctorFormValues } from '../schemas/doctor.schema';
import type { WorkHistoryEntry } from '../types/doctor.types';
import { DoctorCard, DoctorField, doctorControlClass } from './doctor-form-primitives';

interface Props {
  register: UseFormRegister<DoctorFormValues>;
  errors: FieldErrors<DoctorFormValues>;
  history: WorkHistoryEntry[];
  onHistoryChange: (history: WorkHistoryEntry[]) => void;
  biographyLength: number;
}

export function DoctorCreateCredentials({ register, errors }: Pick<Props, 'register' | 'errors'>) {
  return (
    <DoctorCard title="3. TRÌNH ĐỘ & CHỨNG CHỈ HÀNH NGHỀ (CCHN)" note="Pháp lý Y tế">
      <div className="grid grid-cols-[1fr_120px] gap-4">
        <DoctorField label="Học hàm / Học vị" required error={errors.degree?.message}>
          <select className={doctorControlClass} {...register('degree')}>
            <option value="">Chọn học hàm/học vị</option>
            {DEGREES.map((item) => (
              <option key={item}>{item}</option>
            ))}
          </select>
        </DoctorField>
        <DoctorField label="Số năm kinh nghiệm" required error={errors.experience?.message}>
          <input
            type="number"
            className={doctorControlClass}
            {...register('experience', { valueAsNumber: true })}
          />
        </DoctorField>
      </div>
      <div className="mt-4 space-y-4">
        <DoctorField
          label="Chuyên khoa Chuyên sâu"
          required
          hint="Từ khóa hiển thị trên bộ lọc tìm kiếm của Cổng Bệnh nhân."
          error={errors.specialty?.message}
        >
          <input className={doctorControlClass} {...register('specialty')} />
        </DoctorField>
        <DoctorField label="Số Chứng chỉ Hành nghề CCHN" required error={errors.license?.message}>
          <div className="flex gap-2">
            <input className={doctorControlClass} {...register('license')} />
            <span className="mt-1.5 flex shrink-0 items-center gap-1 rounded border border-emerald-200 bg-emerald-50 px-2 text-[10px] text-emerald-700">
              <img src="/assets/admin-doctors/create-12.svg" alt="" />
              Chưa xác minh · minh họa
            </span>
          </div>
        </DoctorField>
        <DoctorField
          label="Phạm vi hoạt động chuyên môn theo CCHN"
          hint="Nhập phạm vi chuyên môn theo hồ sơ minh họa."
        >
          <input className={doctorControlClass} {...register('scope')} />
        </DoctorField>
      </div>
    </DoctorCard>
  );
}

export function DoctorCreateBiography({
  register,
  errors,
  history,
  onHistoryChange,
  biographyLength,
}: Props) {
  const update = (index: number, property: keyof WorkHistoryEntry, value: string) =>
    onHistoryChange(
      history.map((item, itemIndex) =>
        itemIndex === index ? { ...item, [property]: value } : item,
      ),
    );
  return (
    <DoctorCard title="4. TIỂU SỬ CHUYÊN MÔN & LỊCH SỬ CÔNG TÁC" note="Hiển thị Cổng Bệnh nhân">
      <DoctorField label="Tóm tắt tiểu sử chuyên môn (Biography)" error={errors.biography?.message}>
        <textarea rows={5} className={doctorControlClass} {...register('biography')} />
      </DoctorField>
      <div className="mt-1 flex justify-between text-[10px] text-slate-400">
        <span>Khuyến nghị từ 150 - 300 từ ngữ y khoa chuẩn mực</span>
        <span>{biographyLength}/1000 ký tự</span>
      </div>
      <div className="mt-6 flex items-center justify-between gap-2 text-[11px] font-semibold">
        <span>Quá trình đào tạo & Đơn vị công tác (Work History)</span>
        <button
          type="button"
          className="text-primary"
          onClick={() => onHistoryChange([...history, { year: '', description: '' }])}
        >
          ⊕ Thêm mốc đào tạo
        </button>
      </div>
      <div className="mt-3 space-y-2 border-l-2 border-sky-100 pl-4">
        {history.map((item, index) => (
          <div
            key={index}
            className="flex items-start gap-2 rounded-md border border-border bg-canvas p-2"
          >
            <div className="min-w-0 flex-1">
              <input
                aria-label={`Năm mốc ${index + 1}`}
                value={item.year}
                onChange={(event) => update(index, 'year', event.target.value)}
                placeholder="Năm"
                className="w-full bg-transparent text-[10px] font-bold text-primary outline-none"
              />
              <input
                aria-label={`Mô tả mốc ${index + 1}`}
                value={item.description}
                onChange={(event) => update(index, 'description', event.target.value)}
                placeholder="Mô tả quá trình đào tạo / công tác"
                className="w-full bg-transparent text-[11px] outline-none"
              />
            </div>
            <button
              type="button"
              aria-label={`Xóa mốc ${index + 1}`}
              onClick={() => onHistoryChange(history.filter((_, itemIndex) => itemIndex !== index))}
              className="text-slate-400 hover:text-rose-600"
            >
              <img src="/assets/admin-doctors/create-16.svg" alt="" />
            </button>
          </div>
        ))}
      </div>
      <button
        type="button"
        className="mt-3 w-full rounded border border-dashed border-slate-300 px-3 py-2 text-[11px] text-sub"
        onClick={() => onHistoryChange([...history, { year: '', description: '' }])}
      >
        <img src="/assets/admin-doctors/create-17.svg" alt="" className="mr-1 inline-block" />
        Thêm mốc đào tạo / công tác mới
      </button>
    </DoctorCard>
  );
}
