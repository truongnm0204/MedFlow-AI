import { Plus, RotateCw } from 'lucide-react';

import { Button } from '@/shared/ui/button';

interface ServiceCreateActionBarProps {
  onCancel: () => void;
  onSaveDraft: () => void;
}

export function ServiceCreateActionBar({ onCancel, onSaveDraft }: ServiceCreateActionBarProps) {
  return (
    <div className="sticky bottom-0 z-20 border-t border-border bg-white px-6 py-3 shadow-lg">
      <div className="flex flex-wrap items-center justify-between gap-4 text-xs text-slate-600">
        <div className="flex items-center gap-2">
          <RotateCw className="h-3.5 w-3.5 text-primary animate-spin-slow" />
          <span>Dịch vụ hiển thị trên cổng khi được bật và có slot hợp lệ.</span>
        </div>

        <div className="flex items-center gap-2.5">
          <Button type="button" variant="secondary" size="sm" onClick={onCancel}>
            Hủy bỏ [Esc]
          </Button>
          <Button type="button" variant="secondary" size="sm" onClick={onSaveDraft}>
            Lưu bản nháp
          </Button>
          <Button id="service-create-submit" type="submit" size="sm" className="gap-1.5 font-bold">
            <Plus className="h-4 w-4" />
            <span>Tạo dịch vụ nháp [Ctrl+S]</span>
          </Button>
        </div>
      </div>
    </div>
  );
}
