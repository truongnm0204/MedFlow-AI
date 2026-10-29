import { Search, SlidersHorizontal } from 'lucide-react';
import { useState } from 'react';

import { Button } from '@/shared/ui/button';

interface DoctorSearchBarProps {
  initialKeyword?: string;
  onSearch: (keyword: string) => void;
}

export function DoctorSearchBar({ initialKeyword = '', onSearch }: DoctorSearchBarProps) {
  const [keyword, setKeyword] = useState(initialKeyword);

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onSearch(keyword);
  };

  return (
    <form onSubmit={handleSubmit} className="relative flex w-full items-center gap-2.5">
      <div className="relative flex-1">
        <Search
          className="absolute left-3.5 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400"
          aria-hidden="true"
        />
        <input
          type="text"
          value={keyword}
          onChange={(e) => setKeyword(e.target.value)}
          placeholder="Tìm kiếm theo tên bác sĩ, học hàm, chuyên khoa khám bệnh..."
          className="h-11 w-full rounded-xl border border-slate-200/90 bg-white pl-10 pr-4 text-sm text-slate-900 placeholder-slate-400 shadow-sm transition-all focus:border-sky-500 focus:outline-none focus:ring-2 focus:ring-sky-500/20"
        />
      </div>

      <Button
        type="submit"
        className="h-11 shrink-0 gap-2 rounded-xl bg-sky-600 px-5 text-sm font-semibold text-white shadow-sm hover:bg-sky-700"
      >
        <SlidersHorizontal className="h-4 w-4" aria-hidden="true" />
        <span>Tìm kiếm</span>
      </Button>
    </form>
  );
}
