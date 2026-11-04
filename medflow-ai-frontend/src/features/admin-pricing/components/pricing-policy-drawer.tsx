import { BookOpen, CheckCircle, FileText, Info, Scale, ShieldCheck, X } from 'lucide-react';
import { useEffect } from 'react';

interface PricingPolicyDrawerProps {
  isOpen: boolean;
  onClose: () => void;
}

export function PricingPolicyDrawer({ isOpen, onClose }: PricingPolicyDrawerProps) {
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose();
      }
    };
    if (isOpen) {
      window.addEventListener('keydown', handleKeyDown);
    }
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="pricing-policy-drawer-title"
      className="fixed inset-0 z-50 flex justify-end bg-slate-900/40 backdrop-blur-xs animate-in fade-in duration-200"
    >
      <div className="flex h-full w-full max-w-md flex-col bg-white shadow-2xl animate-in slide-in-from-right duration-200 border-l border-border">
        {/* Header */}
        <div className="flex items-center justify-between border-b border-border px-6 py-4 bg-slate-50">
          <div className="flex items-center gap-2">
            <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-sky-100 text-primary">
              <Scale className="h-4 w-4" aria-hidden="true" />
            </div>
            <div>
              <h2 id="pricing-policy-drawer-title" className="text-sm font-bold text-ink">
                Cơ Sở Pháp Lý & Quy Định Biểu Phí
              </h2>
              <p className="text-[10px] font-mono text-sub">UC52: Quản lý Biểu phí Cơ sở</p>
            </div>
          </div>
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg p-1 text-slate-400 hover:bg-slate-200 hover:text-slate-700 transition-colors"
            aria-label="Đóng ngăn quy định biểu phí"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {/* Scrollable Body */}
        <div className="flex-1 overflow-y-auto p-6 space-y-5 text-xs text-slate-700">
          {/* Section 1 */}
          <div className="space-y-2">
            <h3 className="flex items-center gap-1.5 font-bold text-ink text-xs uppercase tracking-wide">
              <BookOpen className="h-4 w-4 text-primary" aria-hidden="true" />
              1. Căn cứ pháp lý định giá dịch vụ y tế
            </h3>
            <p className="text-sub leading-relaxed">
              Biểu phí khám bệnh, chữa bệnh ngoại trú và kỹ thuật cận lâm sàng tại MedFlow AI được
              xây dựng và công khai tuân thủ theo:
            </p>
            <ul className="space-y-1.5 pl-4 text-slate-600 list-disc">
              <li>
                <strong>Luật Khám bệnh, chữa bệnh số 15/2023/QH15</strong> về quyền tự chủ và minh
                bạch giá dịch vụ y tế.
              </li>
              <li>
                <strong>Nghị định số 96/2023/NĐ-CP</strong> quy định chi tiết một số điều của Luật
                Khám bệnh, chữa bệnh.
              </li>
              <li>
                <strong>Thông tư của Bộ Y tế</strong> về phương pháp định giá dịch vụ khám chữa bệnh
                theo 4 yếu tố cấu thành chi phí.
              </li>
            </ul>
          </div>

          {/* Section 2 */}
          <div className="space-y-2 rounded-xl border border-border bg-slate-50/70 p-4">
            <h3 className="flex items-center gap-1.5 font-bold text-ink text-xs uppercase tracking-wide">
              <FileText className="h-4 w-4 text-primary" aria-hidden="true" />
              2. 4 Yếu tố cấu thành giá dịch vụ (UC52)
            </h3>
            <div className="space-y-2 text-[11px] text-slate-600">
              <div className="flex items-start gap-2">
                <CheckCircle className="h-3.5 w-3.5 text-emerald-600 shrink-0 mt-0.5" />
                <span>
                  <strong>Chi phí trực tiếp:</strong> Thuốc, sinh phẩm, hóa chất xét nghiệm, vật tư
                  tiêu hao dùng trong thủ thuật/khám.
                </span>
              </div>
              <div className="flex items-start gap-2">
                <CheckCircle className="h-3.5 w-3.5 text-emerald-600 shrink-0 mt-0.5" />
                <span>
                  <strong>Chi phí tiền lương:</strong> Thù lao bác sĩ khám, điều dưỡng phụ tá, kỹ
                  thuật viên chẩn đoán hình ảnh.
                </span>
              </div>
              <div className="flex items-start gap-2">
                <CheckCircle className="h-3.5 w-3.5 text-emerald-600 shrink-0 mt-0.5" />
                <span>
                  <strong>Chi phí khấu hao:</strong> Máy móc y tế hiện đại (ECG, Siêu âm Doppler,
                  Nội soi ống mềm) và cơ sở hạ tầng.
                </span>
              </div>
              <div className="flex items-start gap-2">
                <CheckCircle className="h-3.5 w-3.5 text-emerald-600 shrink-0 mt-0.5" />
                <span>
                  <strong>Chi phí quản lý:</strong> Điện, nước, phần mềm điều hành phòng khám
                  MedFlow AI và chi phí kiểm chuẩn chất lượng.
                </span>
              </div>
            </div>
          </div>

          {/* Section 3 */}
          <div className="space-y-2">
            <h3 className="flex items-center gap-1.5 font-bold text-ink text-xs uppercase tracking-wide">
              <ShieldCheck className="h-4 w-4 text-emerald-600" aria-hidden="true" />
              3. Quy trình phê duyệt & niêm yết
            </h3>
            <p className="text-sub leading-relaxed">
              Mỗi mức giá áp dụng phải đi kèm số Quyết định ban hành (Ví dụ:{' '}
              <code className="font-mono text-primary font-bold">QĐ-142/BV-KH</code>) và ngày bắt
              đầu hiệu lực cụ thể. Mọi điều chỉnh tăng/giảm giá đều phải lưu lại phiên bản lịch sử
              để phục vụ thanh quyết toán và đối soát BHYT/viện phí.
            </p>
          </div>

          {/* Notice for Update Feature */}
          <div className="rounded-xl border border-sky-200 bg-sky-50 p-4 text-sky-900 space-y-1.5">
            <div className="flex items-center gap-1.5 font-bold text-primary">
              <Info className="h-4 w-4 shrink-0" />
              <span>Chức năng Cập nhật Biểu phí (Issue #207)</span>
            </div>
            <p className="text-[11px] leading-relaxed text-sky-800">
              Quyền điều chỉnh hoặc thêm mới biểu phí được giới hạn cho Hospital Admin có thẩm quyền
              thông qua tính năng <strong>Update Service Price (#207)</strong>. Màn hình hiện tại
              chịu trách nhiệm hiển thị mức giá hiện hành và lịch sử biến động.
            </p>
          </div>
        </div>

        {/* Footer */}
        <div className="border-t border-border bg-slate-50 px-6 py-3.5 flex justify-end">
          <button
            type="button"
            onClick={onClose}
            className="rounded-lg border border-border bg-white px-4 py-2 text-xs font-semibold text-slate-700 shadow-xs hover:bg-slate-50 transition-colors"
          >
            Đã hiểu quy định
          </button>
        </div>
      </div>
    </div>
  );
}
