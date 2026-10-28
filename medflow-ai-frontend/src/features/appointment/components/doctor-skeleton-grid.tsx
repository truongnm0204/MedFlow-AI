export function DoctorSkeletonGrid({ count = 6 }: { count?: number }) {
  return (
    <div className="grid grid-cols-1 gap-5 md:grid-cols-2 lg:grid-cols-3">
      {Array.from({ length: count }).map((_, idx) => (
        <div
          key={idx}
          className="flex h-[280px] animate-pulse flex-col justify-between rounded-2xl border border-slate-200/80 bg-white p-5 shadow-sm"
        >
          <div>
            <div className="flex items-start gap-3.5">
              <div className="h-14 w-14 rounded-xl bg-slate-200" />
              <div className="flex-1 space-y-2">
                <div className="h-4 w-28 rounded bg-slate-200" />
                <div className="h-5 w-40 rounded bg-slate-200" />
                <div className="h-3 w-48 rounded bg-slate-100" />
              </div>
            </div>

            <div className="mt-3.5 h-8 w-full rounded-lg bg-slate-100" />
            <div className="mt-3 h-14 w-full rounded-lg bg-slate-100" />
          </div>

          <div className="border-t border-slate-100 pt-3">
            <div className="flex justify-between">
              <div className="h-3 w-32 rounded bg-slate-100" />
              <div className="h-4 w-24 rounded bg-slate-200" />
            </div>
            <div className="mt-3 grid grid-cols-2 gap-2.5">
              <div className="h-8 rounded-lg bg-slate-200" />
              <div className="h-8 rounded-lg bg-slate-200" />
            </div>
          </div>
        </div>
      ))}
    </div>
  );
}
