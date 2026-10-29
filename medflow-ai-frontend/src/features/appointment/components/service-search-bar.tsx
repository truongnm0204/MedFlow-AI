import { useState } from 'react';
import { Search } from 'lucide-react';

interface ServiceSearchBarProps {
  initialValue?: string;
  onSearch: (value: string) => void;
}

export function ServiceSearchBar({ initialValue = '', onSearch }: ServiceSearchBarProps) {
  const [keyword, setKeyword] = useState(initialValue);
  const [prevInitialValue, setPrevInitialValue] = useState(initialValue);

  if (prevInitialValue !== initialValue) {
    setPrevInitialValue(initialValue);
    setKeyword(initialValue);
  }

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    onSearch(keyword);
  };

  return (
    <form onSubmit={handleSubmit} className="relative flex items-center w-full gap-2">
      <div className="relative flex-1">
        <div className="absolute inset-y-0 left-0 flex items-center pl-3.5 pointer-events-none text-slate-400">
          <Search className="w-5 h-5" />
        </div>
        <input
          type="text"
          value={keyword}
          onChange={(e) => setKeyword(e.target.value)}
          placeholder="Tìm theo tên dịch vụ khám, gói khám, bác sĩ hoặc khoa phòng..."
          className="w-full pl-10 pr-4 py-3 bg-white border border-slate-200 rounded-xl text-slate-800 placeholder-slate-400 text-sm focus:outline-none focus:ring-2 focus:ring-sky-500 focus:border-transparent transition-all shadow-sm"
        />
      </div>
      <button
        type="submit"
        className="px-6 py-3 bg-sky-600 hover:bg-sky-700 active:bg-sky-800 text-white font-medium text-sm rounded-xl flex items-center gap-2 transition-colors shadow-sm shrink-0"
      >
        <Search className="w-4 h-4" />
        <span>Tìm kiếm</span>
      </button>
    </form>
  );
}
