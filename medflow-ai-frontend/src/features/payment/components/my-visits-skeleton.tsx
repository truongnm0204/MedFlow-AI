import { Card } from '@/shared/ui/card';

export function MyVisitsSkeleton() {
  return (
    <div className="space-y-3" aria-hidden="true">
      {[0, 1, 2].map((index) => (
        <Card key={index} className="animate-pulse p-5">
          <div className="h-4 w-40 rounded bg-slate-200" />
          <div className="mt-3 h-3 w-56 rounded bg-slate-100" />
          <div className="mt-6 h-9 w-36 rounded-xl bg-slate-100" />
        </Card>
      ))}
    </div>
  );
}
