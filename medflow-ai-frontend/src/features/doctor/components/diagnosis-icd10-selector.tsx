import { Search } from 'lucide-react';
import { useEffect, useState } from 'react';

import { getErrorMessage } from '@/shared/lib/get-error-message';
import { Spinner } from '@/shared/ui/spinner';

import { useIcd10CatalogSearch } from '../hooks/use-consultation-workspace';
import type { Icd10CatalogItemDto } from '../types/consultation-workspace.types';

export interface DiagnosisIcd10SelectorProps {
  id: string;
  label: string;
  primaryOnly?: boolean;
  disabledCodes?: string[];
  onSelect: (option: Icd10CatalogItemDto) => void;
  placeholder?: string;
  error?: string;
}

export function DiagnosisIcd10Selector({
  id,
  label,
  primaryOnly = false,
  disabledCodes = [],
  onSelect,
  placeholder = 'Tìm mã hoặc tên bệnh ICD-10 (ví dụ: K29.5, viêm dạ dày)',
  error,
}: DiagnosisIcd10SelectorProps) {
  const [query, setQuery] = useState('');
  const [debouncedQuery, setDebouncedQuery] = useState('');
  const [open, setOpen] = useState(false);

  useEffect(() => {
    const timer = setTimeout(() => setDebouncedQuery(query), 300);
    return () => clearTimeout(timer);
  }, [query]);

  const search = useIcd10CatalogSearch({ q: debouncedQuery, primaryOnly }, open);

  const listId = `${id}-listbox`;
  const errorId = `${id}-error`;
  const options = search.data ?? [];

  return (
    <div className="relative">
      <label htmlFor={id} className="mb-1 block text-xs font-semibold text-navy">
        {label}
      </label>
      <div className="relative">
        <Search
          className="pointer-events-none absolute top-1/2 left-3 h-4 w-4 -translate-y-1/2 text-sub"
          aria-hidden="true"
        />
        <input
          id={id}
          type="text"
          role="combobox"
          aria-expanded={open}
          aria-controls={listId}
          aria-autocomplete="list"
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? errorId : undefined}
          value={query}
          placeholder={placeholder}
          autoComplete="off"
          onChange={(event) => {
            setQuery(event.target.value);
            setOpen(true);
          }}
          onFocus={() => setOpen(true)}
          onKeyDown={(event) => {
            if (event.key === 'Escape') setOpen(false);
          }}
          className="w-full rounded-lg border border-border bg-white py-2 pr-3 pl-9 text-sm text-ink outline-none focus:border-primary focus:ring-2 focus:ring-primary/20"
        />
      </div>
      {open && (
        <ul
          id={listId}
          role="listbox"
          aria-label={`Kết quả ${label}`}
          className="absolute z-20 mt-1 max-h-60 w-full overflow-y-auto rounded-lg border border-border bg-white py-1 shadow-lg"
        >
          {search.isError ? (
            <li role="alert" className="px-3 py-2 text-xs text-rose-600">
              {getErrorMessage(search.error, 'Không tải được danh mục ICD-10.')}
            </li>
          ) : search.isLoading ? (
            <li className="flex items-center gap-2 px-3 py-2 text-xs text-sub">
              <Spinner className="h-3.5 w-3.5" />
              Đang tìm mã ICD-10…
            </li>
          ) : options.length === 0 ? (
            <li className="px-3 py-2 text-xs text-sub">Không tìm thấy mã ICD-10 phù hợp.</li>
          ) : (
            options.map((option) => {
              const disabled =
                disabledCodes.includes(option.code) ||
                !option.isSelectable ||
                (primaryOnly && option.notPrimaryDx);
              return (
                <li
                  key={`${option.id}-${option.code}`}
                  role="option"
                  aria-selected={false}
                  aria-disabled={disabled}
                  onMouseDown={(event) => event.preventDefault()}
                  onClick={() => {
                    if (disabled) return;
                    onSelect(option);
                    setQuery('');
                    setOpen(false);
                  }}
                  className={`flex cursor-pointer items-baseline gap-2 px-3 py-2 text-sm ${
                    disabled ? 'cursor-not-allowed text-sub opacity-50' : 'text-ink hover:bg-canvas'
                  }`}
                >
                  <span className="font-bold text-navy">{option.code}</span>
                  <span className="min-w-0 flex-1 truncate">{option.nameVi}</span>
                </li>
              );
            })
          )}
        </ul>
      )}
      {error && (
        <p id={errorId} role="alert" className="mt-1 text-xs font-medium text-rose-600">
          {error}
        </p>
      )}
    </div>
  );
}
