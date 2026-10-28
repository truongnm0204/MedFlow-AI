import { Stethoscope, X } from 'lucide-react';
import { useEffect } from 'react';
import { useNavigate } from 'react-router-dom';

import { Button } from '@/shared/ui/button';

import { DoctorDetailSummary } from './doctor-detail-summary';
import { DoctorEducationList } from './doctor-education-list';
import { DoctorExperienceSection } from './doctor-experience-section';
import { DoctorWeeklySchedule } from './doctor-weekly-schedule';
import { useDoctorDetail } from '../hooks/use-doctor-detail';
import { useBookingStore } from '../store/booking.store';

interface DoctorDetailModalProps {
  doctorId: string;
  isOpen: boolean;
  onClose: () => void;
}

export function DoctorDetailModal({ doctorId, isOpen, onClose }: DoctorDetailModalProps) {
  const navigate = useNavigate();
  const selectDoctor = useBookingStore((s) => s.selectDoctor);
  const { data: doctor, isLoading, isError } = useDoctorDetail(doctorId);

  useEffect(() => {
    if (!isOpen) return;

    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose();
      }
    };

    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  const handleBookAppointment = () => {
    if (doctor) {
      selectDoctor(doctor.id);
      onClose();
      navigate(`/appointment/booking?doctorId=${doctor.id}`);
    }
  };

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="doctor-detail-title"
      className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/60 p-3 sm:p-4 backdrop-blur-xs animate-in fade-in duration-200"
      onClick={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="flex max-h-[92vh] w-full max-w-2xl flex-col overflow-hidden rounded-2xl border border-slate-200/80 bg-white shadow-2xl">
        {/* Header */}
        <div className="flex items-start justify-between border-b border-slate-100 px-5 py-4 sm:px-6">
          <div className="flex items-center gap-3">
            <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-sky-50 text-sky-600 shadow-2xs">
              <Stethoscope className="h-6 w-6" />
            </div>

            <div>
              <h2
                id="doctor-detail-title"
                className="text-lg font-bold tracking-tight text-slate-900 sm:text-xl"
              >
                Hồ sơ Chuyên môn Bác sĩ
              </h2>
              <p className="text-xs text-slate-500">
                Thông tin học vị, lĩnh vực chuyên sâu & lịch thăm khám ngoại trú tự nguyện
              </p>
            </div>
          </div>

          <button
            type="button"
            onClick={onClose}
            aria-label="Đóng hồ sơ bác sĩ"
            className="rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-700 transition-colors"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Scrollable Body Content */}
        <div className="flex-1 overflow-y-auto px-5 py-5 sm:px-6 space-y-6">
          {isLoading && (
            <div className="space-y-4 py-8">
              <div className="h-28 animate-pulse rounded-2xl bg-slate-100" />
              <div className="h-36 animate-pulse rounded-2xl bg-slate-100" />
              <div className="h-28 animate-pulse rounded-2xl bg-slate-100" />
            </div>
          )}

          {isError && (
            <div className="rounded-xl border border-rose-200 bg-rose-50 p-6 text-center text-sm text-rose-700">
              Không thể tải thông tin hồ sơ bác sĩ. Vui lòng thử lại sau.
            </div>
          )}

          {doctor && (
            <>
              {/* Doctor Summary Info */}
              <DoctorDetailSummary doctor={doctor} />

              {/* Education & Credentials */}
              <DoctorEducationList education={doctor.education} />

              {/* Clinical Experience & Specialties */}
              <DoctorExperienceSection biography={doctor.biography} hashtags={doctor.hashtags} />

              {/* Weekly Duty Schedule */}
              <DoctorWeeklySchedule schedule={doctor.weeklySchedule} />
            </>
          )}
        </div>

        {/* Footer Actions */}
        <div className="flex items-center justify-between gap-3 border-t border-slate-100 bg-slate-50/60 px-5 py-3.5 sm:px-6">
          <Button
            type="button"
            variant="secondary"
            onClick={onClose}
            className="text-xs font-semibold text-slate-700"
          >
            Đóng / Quay lại danh bạ
          </Button>

          <Button
            type="button"
            disabled={!doctor}
            onClick={handleBookAppointment}
            className="bg-sky-600 text-xs font-semibold text-white shadow-sm hover:bg-sky-700"
          >
            Xem lịch & Đặt khám ngay
          </Button>
        </div>
      </div>
    </div>
  );
}
