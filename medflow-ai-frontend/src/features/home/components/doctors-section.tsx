import { ArrowRight, Star } from 'lucide-react';
import { Link } from 'react-router-dom';

import { MOCK_DOCTORS } from '../data/home-catalog';

/** Đội ngũ bác sĩ tiêu biểu (anchor #doctors). */
export function DoctorsSection() {
  return (
    <section id="doctors" className="scroll-mt-24 border-t border-border bg-white py-16">
      <div className="mx-auto max-w-7xl px-4 sm:px-6 lg:px-8">
        <div className="mb-12 flex flex-col justify-between gap-4 md:flex-row md:items-end">
          <div>
            <div className="mb-2 inline-flex items-center gap-1.5 rounded-full bg-sky-50 px-3 py-0.5 text-xs font-semibold text-primary">
              ĐỘI NGŨ CHUYÊN GIA
            </div>
            <h2 className="text-2xl font-extrabold tracking-tight text-navy sm:text-3xl">
              Bác Sĩ Chuyên Khoa II & Tiến Sĩ Trực Khám
            </h2>
            <p className="mt-1 text-xs text-sub sm:text-sm">
              Khám trực tiếp, tận tâm, giải thích phác đồ điều trị chi tiết, không lạm dụng thuốc.
            </p>
          </div>
          <Link
            to="/doctors"
            className="inline-flex items-center gap-1 text-xs font-bold text-primary transition-colors hover:text-primary-dark"
          >
            <span>Xem danh sách Bác sĩ</span>
            <ArrowRight className="h-3.5 w-3.5" aria-hidden="true" />
          </Link>
        </div>

        <div className="grid grid-cols-1 gap-6 sm:grid-cols-2 lg:grid-cols-4">
          {MOCK_DOCTORS.map((doc) => (
            <div
              key={doc.id}
              className="flex flex-col justify-between rounded-2xl border border-border bg-canvas p-5 shadow-2xs transition-all hover:shadow-md"
            >
              <div className="space-y-3">
                <div className="flex items-center gap-3">
                  <div
                    aria-hidden="true"
                    className="flex h-12 w-12 shrink-0 items-center justify-center rounded-full bg-primary text-sm font-bold text-white shadow-md shadow-sky-600/20"
                  >
                    {doc.avatar}
                  </div>
                  <div>
                    <h3 className="text-xs leading-tight font-bold text-ink">{doc.name}</h3>
                    <span className="text-[10px] font-semibold text-primary">{doc.role}</span>
                  </div>
                </div>

                <div className="space-y-1 text-[11px] text-sub">
                  <div>{doc.degree}</div>
                  <div className="font-medium text-ink">Đơn vị: {doc.hospital}</div>
                  {doc.rating.trim() !== '' && (
                    <div className="flex items-center gap-1 font-semibold text-amber-600">
                      <Star className="h-3.5 w-3.5 shrink-0 fill-current" aria-hidden="true" />
                      <span>{doc.rating}</span>
                    </div>
                  )}
                </div>
              </div>

              <div className="mt-4 space-y-2 border-t border-border pt-4">
                {doc.earliestSlot.trim() !== '' && (
                  <div className="rounded-lg border border-border bg-white p-2 text-[10px]">
                    <span className="block text-sub">Lịch khám trực tuyến:</span>
                    <strong className="font-mono text-emerald-700">{doc.earliestSlot}</strong>
                  </div>
                )}

                <Link
                  to="/doctors"
                  className="block w-full rounded-lg bg-primary py-1.5 text-center text-xs font-bold text-white transition-colors hover:bg-primary-dark"
                >
                  {doc.fee.trim() !== '' ? `Đặt hẹn bác sĩ (${doc.fee})` : 'Tra cứu lịch & Đặt hẹn'}
                </Link>
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
