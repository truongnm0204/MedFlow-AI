export function ServiceSkeletonGrid() {
  return (
    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5 animate-pulse">
      {Array.from({ length: 6 }).map((_, idx) => (
        <div
          key={idx}
          className="bg-white rounded-2xl border border-slate-200/80 p-5 flex flex-col justify-between h-[320px]"
        >
          <div>
            <div className="flex justify-between items-center mb-3">
              <div className="h-4 bg-slate-200 rounded w-1/3" />
              <div className="h-4 bg-slate-200 rounded-full w-1/4" />
            </div>
            <div className="h-6 bg-slate-200 rounded w-3/4 mb-3" />
            <div className="h-4 bg-slate-200 rounded w-1/2 mb-3" />
            <div className="h-3 bg-slate-200 rounded w-full mb-2" />
            <div className="h-3 bg-slate-200 rounded w-4/5 mb-4" />
            <div className="flex gap-2">
              <div className="h-4 bg-slate-200 rounded w-16" />
              <div className="h-4 bg-slate-200 rounded w-20" />
            </div>
          </div>
          <div>
            <div className="h-8 bg-slate-200 rounded w-1/2 mb-3" />
            <div className="flex gap-2">
              <div className="h-9 bg-slate-200 rounded-xl flex-1" />
              <div className="h-9 bg-slate-200 rounded-xl flex-1" />
            </div>
          </div>
        </div>
      ))}
    </div>
  );
}

