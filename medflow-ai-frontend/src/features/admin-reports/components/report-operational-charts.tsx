import {
  DEPARTMENT_VISITS_DATA,
  HOURLY_INTAKE_DATA,
  VISIT_STATUS_SEGMENTS,
} from '../data/operational-reports-data';

const Y_AXIS_TICKS = [40, 30, 20, 10, 0] as const;
const MAX_HOURLY_SCALE = 44;
const MAX_DEPT_COUNT = 88;

export function ReportOperationalCharts() {
  return (
    <section className="space-y-4" aria-label="Biểu đồ diễn biến và phân bổ vận hành">
      <div>
        <h2 className="text-lg font-bold text-ink">Diễn biến &amp; phân bổ</h2>
        <p className="mt-0.5 text-xs text-sub">Dữ liệu minh họa · 22/10/2026 · Cơ sở Hà Nội</p>
      </div>

      {/* Chart Card 1: Lượt tiếp nhận theo giờ */}
      <div className="rounded-lg border border-border bg-white p-6 shadow-sm">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <h3 className="text-base font-bold text-ink">Lượt tiếp nhận theo giờ</h3>
            <p className="mt-0.5 text-xs text-sub">Lượt tiếp nhận trong ngày · Tổng 326 lượt</p>
          </div>
          <span className="inline-flex items-center rounded-full border border-sky-200 bg-sky-50 px-3 py-1 text-xs font-semibold text-primary">
            Cao điểm 11:00 · 42 lượt
          </span>
        </div>

        <div className="mt-6">
          <div className="relative h-56 pl-8">
            {/* Horizontal grid lines & Y-axis labels */}
            <div className="pointer-events-none absolute inset-0 flex flex-col justify-between pb-6">
              {Y_AXIS_TICKS.map((tick) => (
                <div key={tick} className="flex items-center gap-2">
                  <span className="w-6 text-right font-mono text-[11px] text-sub">{tick}</span>
                  <div className="h-px flex-1 border-b border-dashed border-border" />
                </div>
              ))}
            </div>

            {/* Hourly vertical bars */}
            <div className="relative z-10 flex h-full items-end justify-between gap-2 pl-2">
              {HOURLY_INTAKE_DATA.map((point) => {
                const heightPercent = Math.round((point.count / MAX_HOURLY_SCALE) * 100);
                return (
                  <div
                    key={point.hour}
                    className="flex h-full flex-1 flex-col items-center justify-end"
                  >
                    <div className="flex h-[calc(100%-24px)] w-full flex-col items-center justify-end">
                      <span
                        className={`mb-1.5 font-mono text-[11px] font-bold ${
                          point.isPeak ? 'text-primary' : 'text-ink'
                        }`}
                      >
                        {point.count}
                      </span>
                      <div
                        style={{ height: `${heightPercent}%` }}
                        className={`w-full max-w-11 rounded-t-md transition-all ${
                          point.isPeak
                            ? 'bg-primary shadow-xs ring-2 ring-sky-100'
                            : 'bg-sky-200/80 hover:bg-primary/70'
                        }`}
                        role="img"
                        aria-label={`${point.hour}: ${point.count} lượt`}
                      />
                    </div>
                    <span className="mt-2 font-mono text-[11px] font-medium text-sub">
                      {point.hour}
                    </span>
                  </div>
                );
              })}
            </div>
          </div>
        </div>
      </div>

      {/* Bottom 2-column charts */}
      <div className="grid gap-6 lg:grid-cols-2">
        {/* Chart Card 2: Lượt khám theo khoa */}
        <div className="flex flex-col justify-between rounded-lg border border-border bg-white p-6 shadow-sm">
          <div>
            <h3 className="text-base font-bold text-ink">Lượt khám theo khoa</h3>
            <p className="mt-0.5 text-xs text-sub">Phân bổ 326 lượt theo khoa phòng</p>

            <div className="mt-5 space-y-4">
              {DEPARTMENT_VISITS_DATA.map((item) => {
                const widthPercent = Math.round((item.count / MAX_DEPT_COUNT) * 100);
                return (
                  <div
                    key={item.department}
                    className="grid grid-cols-[110px_1fr_44px] items-center gap-3 text-xs"
                  >
                    <span className="truncate font-semibold text-ink">{item.department}</span>
                    <div className="h-3 overflow-hidden rounded-full bg-slate-100">
                      <div
                        style={{ width: `${widthPercent}%` }}
                        className="h-full rounded-full bg-primary transition-all"
                        role="img"
                        aria-label={`${item.department}: ${item.count} lượt`}
                      />
                    </div>
                    <span className="text-right font-mono text-xs font-bold text-ink">
                      {item.count}
                    </span>
                  </div>
                );
              })}
            </div>
          </div>

          <div className="mt-5 flex items-center justify-between border-t border-slate-100 pt-3 font-mono text-[11px] text-sub">
            <span>0</span>
            <span>88 lượt</span>
          </div>
        </div>

        {/* Chart Card 3: Trạng thái lượt khám */}
        <div className="flex flex-col justify-between rounded-lg border border-border bg-white p-6 shadow-sm">
          <div>
            <h3 className="text-base font-bold text-ink">Trạng thái lượt khám</h3>
            <p className="mt-0.5 text-xs text-sub">Phân bổ tại thời điểm tổng hợp · 326 lượt</p>

            {/* Stacked progress bar */}
            <div
              className="mt-5 flex h-4 w-full overflow-hidden rounded-full bg-slate-100 p-0.5 gap-0.5"
              role="img"
              aria-label="Tỷ lệ trạng thái lượt khám: Hoàn tất 94%, Đang khám 4%, Chờ khám 2%"
            >
              {VISIT_STATUS_SEGMENTS.map((segment) => (
                <div
                  key={segment.key}
                  style={{ width: `${segment.percentValue}%` }}
                  className={`h-full first:rounded-l-full last:rounded-r-full ${segment.barColorClass}`}
                />
              ))}
            </div>

            {/* Status Legend Breakdown */}
            <div className="mt-5 divide-y divide-slate-100">
              {VISIT_STATUS_SEGMENTS.map((segment) => (
                <div key={segment.key} className="flex items-center justify-between py-3 text-xs">
                  <div className="flex items-center gap-2.5">
                    <span
                      className={`h-3 w-3 shrink-0 rounded-xs ${segment.swatchColorClass}`}
                      aria-hidden="true"
                    />
                    <span className="font-semibold text-ink">{segment.label}</span>
                  </div>
                  <div className="flex items-center gap-4">
                    <span className="text-sub">{segment.countLabel}</span>
                    <span className="w-10 text-right font-mono font-bold text-ink">
                      {segment.percentLabel}
                    </span>
                  </div>
                </div>
              ))}
            </div>
          </div>

          <p className="mt-4 border-t border-slate-100 pt-3 text-[11px] text-sub">
            Tỷ lệ hoàn tất được tính trên tổng lượt tiếp nhận.
          </p>
        </div>
      </div>
    </section>
  );
}
