import { useCallback } from 'react';
import { useNavigate } from 'react-router-dom';

import type { FinalizeEkycSessionResponse } from '../types/ekyc.types';
import { EkycCaptureWizard } from '../components/ekyc-capture-wizard';

/** Phiên bản điều khoản đồng ý — phải khớp `Ekyc:ConsentVersion` mặc định của backend. */
export const EKYC_CONSENT_VERSION = 'ekyc-consent-v1';

/**
 * PLACEHOLDER — final consent copy comes from PM/owner legal review (Luật 91/2025);
 * replace before production.
 */
export const EKYC_CONSENT_TEXT_PLACEHOLDER = `Tôi đồng ý cho MedFlow xử lý dữ liệu cá nhân của tôi để xác thực danh tính điện tử.

1. Dữ liệu được xử lý: ảnh mặt trước và mặt sau giấy tờ tùy thân, ảnh khuôn mặt, dữ liệu sinh trắc học và thông tin cá nhân in trên giấy tờ.
2. Mục đích: xác minh danh tính, đối chiếu khuôn mặt với giấy tờ, tạo và kích hoạt tài khoản bệnh nhân, bảo vệ tài khoản khỏi bị mạo danh.
3. Cách thức xử lý: dữ liệu được gửi tới nhà cung cấp dịch vụ eKYC đã được lựa chọn và được lưu trữ, bảo mật theo quy định pháp luật hiện hành.
4. Quyền của tôi: tôi có thể từ chối hoặc rút lại sự đồng ý bất kỳ lúc nào, yêu cầu xem, chỉnh sửa hoặc xóa dữ liệu theo quy định. Nếu không đồng ý, tôi có thể được xác minh trực tiếp tại quầy tiếp nhận.
5. Thời hạn lưu trữ và đầu mối liên hệ xử lý dữ liệu cá nhân được quy định trong chính sách quyền riêng tư của MedFlow.`;

export interface EkycVerificationPageProps {
  /** Ghi đè hành vi sau khi xác minh thành công; mặc định quay về trang chủ. */
  onVerified?: (result: FinalizeEkycSessionResponse) => void;
  /** Ghi đè hành vi khi người dùng thoát; mặc định quay về trang chủ. */
  onCancel?: () => void;
}

export function EkycVerificationPage({ onVerified, onCancel }: EkycVerificationPageProps = {}) {
  const navigate = useNavigate();

  const handleVerified = useCallback(
    (result: FinalizeEkycSessionResponse) => {
      if (onVerified) {
        onVerified(result);
        return;
      }
      navigate('/', { replace: true });
    },
    [navigate, onVerified],
  );

  const handleCancel = useCallback(() => {
    if (onCancel) {
      onCancel();
      return;
    }
    navigate('/', { replace: true });
  }, [navigate, onCancel]);

  return (
    <main className="mx-auto w-full max-w-4xl px-4 py-8 sm:px-6">
      <header className="mb-6">
        <h1 className="text-xl font-bold text-ink sm:text-2xl">Xác minh danh tính điện tử</h1>
        <p className="mt-1 text-sm text-sub">
          Xác minh giấy tờ tùy thân và khuôn mặt để hoàn tất đăng ký tài khoản bệnh nhân.
        </p>
      </header>

      <EkycCaptureWizard
        consentVersion={EKYC_CONSENT_VERSION}
        consentText={EKYC_CONSENT_TEXT_PLACEHOLDER}
        onVerified={handleVerified}
        onCancel={handleCancel}
      />
    </main>
  );
}
