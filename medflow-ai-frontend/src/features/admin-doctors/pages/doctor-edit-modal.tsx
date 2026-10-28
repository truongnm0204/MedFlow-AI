import { Info } from 'lucide-react';
import { zodResolver } from '@hookform/resolvers/zod';
import { useEffect, useState } from 'react';
import { useForm, useWatch } from 'react-hook-form';
import { useNavigate, useParams } from 'react-router-dom';

import { Button } from '@/shared/ui/button';

import { DoctorEditFooter, DoctorEditHeader } from '../components/doctor-edit-chrome';
import { DEGREES, DEPARTMENTS } from '../data/doctor-demo-data';
import { doctorSchema, type DoctorFormValues } from '../schemas/doctor.schema';
import { useDoctorDemoStore } from '../store/doctor-demo.store';
import { DoctorField, doctorControlClass } from '../components/doctor-form-primitives';

/** SCR-ADM-DOC-03 — edit dialog layered over the list as in Figma. */
export function DoctorEditModal() {
  const { doctorId } = useParams();
  const navigate = useNavigate();
  const doctor = useDoctorDemoStore((state) => state.doctors.find((item) => item.id === doctorId));
  const updateDoctor = useDoctorDemoStore((state) => state.updateDoctor);
  const [message, setMessage] = useState('');
  const {
    register,
    handleSubmit,
    setValue,
    control,
    formState: { errors },
  } = useForm<DoctorFormValues>({
    resolver: zodResolver(doctorSchema),
    defaultValues: doctor,
  });
  const isActive = useWatch({ control, name: 'active' });
  const biography = useWatch({ control, name: 'biography' }) ?? '';

  useEffect(() => {
    const handleKeys = (event: KeyboardEvent) => {
      if (event.key === 'Escape') navigate('/admin/doctors');
      if (event.ctrlKey && event.key.toLowerCase() === 's') {
        event.preventDefault();
        document.getElementById('doctor-edit-submit')?.click();
      }
    };
    window.addEventListener('keydown', handleKeys);
    return () => window.removeEventListener('keydown', handleKeys);
  }, [navigate]);

  if (!doctor)
    return (
      <div className="fixed inset-0 z-50 grid place-items-center bg-slate-900/60 p-4">
        <div className="rounded-xl bg-white p-8 text-sm">
          <p>Không tìm thấy hồ sơ bác sĩ minh họa.</p>
          <Button className="mt-4" onClick={() => navigate('/admin/doctors')}>
            Quay lại danh sách
          </Button>
        </div>
      </div>
    );
  const save = (values: DoctorFormValues) => {
    updateDoctor({ ...doctor, ...values });
    navigate('/admin/doctors');
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center overflow-y-auto bg-slate-900/60 p-3 backdrop-blur-[2px]"
      role="presentation"
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) navigate('/admin/doctors');
      }}
    >
      <form
        role="dialog"
        aria-modal="true"
        aria-labelledby="doctor-edit-title"
        onSubmit={handleSubmit(save)}
        className="my-auto flex max-h-[calc(100vh-24px)] w-full max-w-[896px] flex-col overflow-hidden rounded-2xl border border-border bg-white shadow-2xl"
      >
        <DoctorEditHeader
          doctor={doctor}
          isActive={isActive}
          onClose={() => navigate('/admin/doctors')}
        />
        <div className="overflow-y-auto">
          <div className="mx-6 mt-5 flex items-start gap-2 rounded-lg border border-sky-200 bg-sky-50 px-4 py-3 text-[11px] leading-relaxed text-sky-900">
            <Info className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
            <p>
              <b>Lưu ý vận hành Khám Ngoại trú:</b> Thông tin buồng khám mặc định và trạng thái nhận
              lịch của Bác sĩ hiện chỉ được chỉnh sửa trên dữ liệu minh họa trong phiên này.
            </p>
          </div>
          {message && (
            <p
              role="status"
              className="mx-6 mt-3 rounded border border-amber-200 bg-amber-50 p-2 text-xs text-amber-700"
            >
              {message}
            </p>
          )}
          <div className="grid gap-x-5 gap-y-4 px-6 py-5 sm:grid-cols-2">
            <DoctorField label="Liên kết Tài khoản Nhân sự (SSO Staff ID)" required>
              <div className="flex gap-2">
                <input
                  readOnly
                  className={`${doctorControlClass} bg-slate-100`}
                  value={`${doctor.staffId} - ${doctor.name} (${doctor.email})`}
                />
                <button
                  type="button"
                  className="mt-1.5 rounded border border-border px-2 text-[10px] text-sub"
                  onClick={() => setMessage('Liên kết SSO chỉ thay đổi tại danh mục Nhân sự.')}
                >
                  Đổi
                </button>
              </div>
            </DoctorField>
            <DoctorField
              label="Khoa công tác trực thuộc"
              required
              error={errors.department?.message}
            >
              <select className={doctorControlClass} {...register('department')}>
                {DEPARTMENTS.map((item) => (
                  <option key={item}>{item}</option>
                ))}
              </select>
            </DoctorField>
            <DoctorField
              label="Mã Bác sĩ (Practitioner ID)"
              required
              hint="Định danh chuẩn quốc tế Practitioner/DOC-0142."
            >
              <input
                readOnly
                className={`${doctorControlClass} bg-slate-100 font-mono`}
                {...register('id')}
              />
            </DoctorField>
            <DoctorField
              label="Chứng chỉ hành nghề (CCHN Bộ Y tế)"
              required
              hint="Số chứng chỉ minh họa; chưa xác minh với Bộ Y tế."
              error={errors.license?.message}
            >
              <input className={doctorControlClass} {...register('license')} />
            </DoctorField>
            <DoctorField label="Họ và tên Bác sĩ" required error={errors.name?.message}>
              <input className={doctorControlClass} {...register('name')} />
            </DoctorField>
            <DoctorField
              label="Buồng khám chỉ định mặc định"
              required
              hint="Tự động gán mã buồng khi lên ca làm việc tại lịch phân công."
              error={errors.room?.message}
            >
              <input className={doctorControlClass} {...register('room')} />
            </DoctorField>
            <DoctorField label="Học hàm / Học vị đào tạo" required error={errors.degree?.message}>
              <select className={doctorControlClass} {...register('degree')}>
                {DEGREES.map((item) => (
                  <option key={item}>{item}</option>
                ))}
              </select>
            </DoctorField>
            <DoctorField label="Điện thoại công vụ / Bàn khám">
              <input className={doctorControlClass} {...register('phone')} />
            </DoctorField>
            <DoctorField
              label="Chuyên khoa chuyên sâu chính"
              required
              error={errors.specialty?.message}
            >
              <input className={doctorControlClass} {...register('specialty')} />
            </DoctorField>
            <label className="flex items-center justify-between gap-2 rounded-lg border border-emerald-200 bg-emerald-50 px-3 text-[11px]">
              <span>
                <b>● Đang nhận lịch khám (Active)</b>
                <small className="block text-sub">
                  Cho phép phân bổ ca khám và đặt hẹn trực tuyến
                </small>
              </span>
              <input
                type="checkbox"
                className="accent-emerald-500"
                checked={isActive}
                onChange={(event) =>
                  setValue('active', event.target.checked, { shouldDirty: true })
                }
              />
            </label>
            <div className="sm:col-span-2">
              <DoctorField
                label="Tóm tắt tiểu sử chuyên môn & Kinh nghiệm lâm sàng (Hiển thị Patient Portal)"
                error={errors.biography?.message}
              >
                <textarea rows={3} className={doctorControlClass} {...register('biography')} />
              </DoctorField>
              <div className="mt-1 flex justify-between text-[10px] text-slate-400">
                <span>Nội dung minh họa chưa được đồng bộ lên Ứng dụng Di động.</span>
                <span>{biography.length} / 1000 ký tự</span>
              </div>
            </div>
          </div>
        </div>
        <DoctorEditFooter
          onClose={() => navigate('/admin/doctors')}
          onSuspend={() => setValue('active', false, { shouldDirty: true })}
        />
      </form>
    </div>
  );
}
