import { Search } from 'lucide-react';
import { useMemo, useState } from 'react';

import type { Icd10Option } from '../types/diagnosis-conclusion.types';

export interface DiagnosisIcd10SelectorProps {
  id: string;
  label: string;
  options: Icd10Option[];
  disabledCodes?: string[];
  onSelect: (option: Icd10Option) => void;
  placeholder?: string;
  error?: string;
}

function normalize(value: string): string {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replace(/đ/g, 'd')
    .toLowerCase();
}

export function DiagnosisIcd10Selector({
  id,
  label,
  options,
  disabledCodes = [],
  onSelect,
  placeholder = 'Tìm mã hoặc tên bệnh ICD-10 (ví dụ: K29.5, viêm dạ dày)',
  error,
}: DiagnosisIcd10SelectorProps) {
  const [query, setQuery] = useState('');
  const [open, setOpen] = useState(false);

  const matches = useMemo(() => {
    const needle = normalize(query.trim());
    return options.filter(
      (option) =>
        !needle || normalize(`${option.code} ${option.nameVi} ${option.nameEn}`).includes(needle),
    );
  }, [options, query]);

  const listId = `${id}-listbox`;
  const errorId = `${id}-error`;

  return (
    <div className="relative">
      <label htmlFor={id} className="mb-1 block text-xs font-semibold text-navy">
        {label}
      </label>
      <div className="relative">
        <Search
          className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-sub"
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
          className="w-full rounded-lg border border-border bg-white py-2 pl-9 pr-3 text-sm text-ink outline-none focus:border-primary focus:ring-2 focus:ring-primary/20"
        />
      </div>
      {open && (
        <ul
          id={listId}
          role="listbox"
          aria-label={`Kết quả ${label}`}
          className="absolute z-20 mt-1 max-h-60 w-full overflow-y-auto rounded-lg border border-border bg-white py-1 shadow-lg"
        >
          {matches.length === 0 ? (
            <li className="px-3 py-2 text-xs text-sub">Không tìm thấy mã ICD-10 phù hợp.</li>
          ) : (
            matches.map((option) => {
              const disabled = disabledCodes.includes(option.code);
              return (
                <li
                  key={option.code}
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
                  <span className="min-w-0 flex-1 truncate">
                    {option.nameVi} ({option.nameEn})
                  </span>
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
