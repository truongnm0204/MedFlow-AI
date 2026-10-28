import { MOCK_DOCTORS } from '../data/home-catalog';

/** Đội ngũ bác sĩ tiêu biểu (anchor #doctors). */
export function DoctorsSection() {
  return (
    <section id="doctors" className="py-16 bg-white border-t border-slate-200 scroll-mt-24">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="flex flex-col md:flex-row md:items-end justify-between mb-12 gap-4">
          <div>
            <div className="inline-flex items-center gap-1.5 px-3 py-0.5 rounded-full bg-sky-50 text-sky-700 text-xs font-semibold mb-2">
              ĐỘI NGŨ CHUYÊN GIA
            </div>
            <h2 className="text-2xl sm:text-3xl font-extrabold text-navy tracking-tight">
              Bác Sĩ Chuyên Khoa II & Tiến Sĩ Trực Khám
            </h2>
            <p className="text-xs sm:text-sm text-slate-500 mt-1">
              Khám trực tiếp, tận tâm, giải thích phác đồ điều trị chi tiết, không lạm dụng thuốc.
            </p>
          </div>
          <a
            href="#doctors"
            className="text-xs font-bold text-sky-600 hover:text-sky-700 flex items-center gap-1"
          >
            Xem toàn bộ 45+ Bác sĩ <span>→</span>
          </a>
        </div>

        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-6">
          {MOCK_DOCTORS.map((doc) => (
            <div
              key={doc.id}
              className="bg-slate-50 rounded-2xl border border-slate-200 p-5 shadow-2xs hover:shadow-md transition-all flex flex-col justify-between"
            >
              <div className="space-y-3">
                <div className="flex items-center gap-3">
                  <div className="w-12 h-12 rounded-full bg-sky-600 text-white font-bold flex items-center justify-center text-sm shadow-md shadow-sky-600/20">
                    {doc.avatar}
                  </div>
                  <div>
                    <h4 className="text-xs font-bold text-slate-900 leading-tight">{doc.name}</h4>
                    <span className="text-[10px] text-sky-700 font-semibold">{doc.role}</span>
                  </div>
                </div>

                <div className="text-[11px] text-slate-500 space-y-1">
                  <div>{doc.degree}</div>
                  <div className="font-medium text-slate-600">Đơn vị: {doc.hospital}</div>
                  <div className="text-amber-600 font-semibold flex items-center gap-1">
                    ★ {doc.rating}
                  </div>
                </div>
              </div>

              <div className="pt-4 border-t border-slate-200 mt-4 space-y-2">
                <div className="p-2 rounded-lg bg-white border border-slate-200 text-[10px]">
                  <span className="text-slate-400 block">Lịch trống sớm nhất:</span>
                  <strong className="text-emerald-700 font-mono">{doc.earliestSlot}</strong>
                </div>

                <a
                  href="#booking"
                  className="w-full py-1.5 bg-sky-600 hover:bg-sky-700 text-white text-xs font-bold rounded-lg text-center block transition-colors"
                >
                  Đặt hẹn bác sĩ ({doc.fee})
                </a>
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
