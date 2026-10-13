import { ShieldCheck } from 'lucide-react';

export function FamilyPolicyBanner() {
  return (
    <div className="mt-6 flex items-start gap-3.5 rounded-2xl border border-sky-200/80 bg-sky-50/60 p-5">
      <ShieldCheck className="mt-0.5 h-5 w-5 shrink-0 text-sky-600" />
      <div>
        <h4 className="text-xs font-bold text-slate-900">
          Quy định quản lý hồ sơ người thân và bảo mật thông tin khám bệnh HL7/FHIR
        </h4>
        <p className="mt-1 text-xs leading-relaxed text-slate-600">
          Mỗi tài khoản khám ngoại trú tự nguyện được liên kết tối đa{' '}
          <strong>05 hồ sơ người thân</strong> trong gia đình. Việc đặt lịch và tra cứu kết quả khám
          bệnh tuân thủ nghiêm ngặt theo quy định bảo mật quyền riêng tư của Bộ Y Tế và tiêu chuẩn
          mã hóa dữ liệu quốc tế. Hồ sơ người thân sẽ được liên kết định danh PID duy nhất trong
          toàn hệ thống MedFlow.
        </p>
      </div>
    </div>
  );
}
