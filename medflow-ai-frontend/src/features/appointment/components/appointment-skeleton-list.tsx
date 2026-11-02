export function AppointmentSkeletonList() {
  return (
    <div aria-busy="true" aria-label="Đang tải danh sách lịch hẹn" className="space-y-4">
      {[1, 2, 3].map((item) => (
        <div
          key={item}
          className="animate-pulse rounded-2xl border border-border bg-white p-6 shadow-2xs"
        >
          <div className="flex items-center justify-between border-b border-slate-100 pb-4">
            <div className="flex items-center gap-3">
              <div className="h-6 w-44 rounded-full bg-slate-200" />
              <div className="h-5 w-36 rounded bg-slate-200" />
            </div>
            <div className="h-8 w-32 rounded-xl bg-slate-200" />
          </div>

          <div className="mt-5 grid grid-cols-1 gap-4 lg:grid-cols-3">
            <div className="space-y-2">
              <div className="h-4 w-28 rounded bg-slate-200" />
              <div className="h-5 w-40 rounded bg-slate-200" />
              <div className="h-4 w-48 rounded bg-slate-100" />
            </div>
            <div className="space-y-2">
              <div className="h-4 w-28 rounded bg-slate-200" />
              <div className="h-5 w-44 rounded bg-slate-200" />
              <div className="h-4 w-52 rounded bg-slate-100" />
            </div>
            <div className="h-24 rounded-xl bg-slate-100" />
          </div>
        </div>
      ))}
    </div>
  );
}
