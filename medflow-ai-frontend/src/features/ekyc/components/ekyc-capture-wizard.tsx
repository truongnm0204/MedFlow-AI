import { useCallback, useEffect, useRef, useState } from 'react';
import { CheckCircle2 } from 'lucide-react';

import {
  Alert,
  Button,
  Card,
  CardContent,
  CardFooter,
  CardHeader,
  Spinner,
} from '@/shared/ui';

import { useEkycFlow } from '../hooks/use-ekyc-flow';
import { EKYC_SDK_CONTAINER_ID, buildEkycSdkConfig } from '../sdk/ekyc-sdk-config';
import { loadVnptEkycSdk } from '../sdk/vnpt-sdk-loader';
import type { FinalizeEkycSessionResponse } from '../types/ekyc.types';
import { getEkycFailureMessage, isRetriableFailure } from '../utils/ekyc-errors';
import { EkycConsentScreen } from './ekyc-consent-screen';

export interface EkycCaptureWizardProps {
  /** Phiên bản điều khoản đồng ý, gửi kèm yêu cầu tạo phiên. */
  consentVersion: string;
  /** Nội dung điều khoản đồng ý hiển thị trước khi bắt đầu. */
  consentText: string;
  /** Được gọi khi người dùng bấm tiếp tục sau khi xác minh thành công. */
  onVerified?: (result: FinalizeEkycSessionResponse) => void;
  /** Thoát khỏi luồng xác minh. */
  onCancel?: () => void;
}

const SDK_LOAD_ERROR =
  'Không thể tải trình xác minh danh tính. Vui lòng kiểm tra kết nối mạng và thử lại.';

/**
 * Thông điệp thân thiện cho các kết quả kích hoạt tài khoản backend trả về.
 * Giá trị lạ (backend thêm mới) rơi về câu trung tính, không hiển thị mã kỹ thuật.
 */
const ACTIVATION_OUTCOME_MESSAGES: Record<string, string> = {
  NEW_IDENTITY_ACTIVATED:
    'Danh tính của bạn đã được xác minh và ghi nhận cho hồ sơ bệnh nhân mới.',
  CLAIM_LINKED_ACTIVATED:
    'Danh tính của bạn đã được xác minh và liên kết với hồ sơ bệnh nhân đã có.',
  FAMILY_MANAGED_PENDING_REVIEW:
    'Danh tính của bạn đã được xác minh. Việc liên kết hồ sơ do người thân quản lý sẽ được bộ phận tiếp nhận xem xét.',
  CONFLICT_ACCOUNT_EXISTS:
    'Số giấy tờ này đã gắn với một tài khoản khác. Vui lòng liên hệ bộ phận hỗ trợ để được hướng dẫn khôi phục tài khoản.',
  INFO_MISMATCH_PENDING_REVIEW:
    'Danh tính của bạn đã được xác minh nhưng thông tin chưa khớp hoàn toàn với hồ sơ hiện có. Bộ phận tiếp nhận sẽ xem xét thêm.',
  MULTIPLE_RECORDS_CONFLICT:
    'Danh tính của bạn đã được xác minh. Hồ sơ hiện có nhiều bản ghi trùng nhau nên cần bộ phận tiếp nhận xem xét.',
  NOT_APPLICABLE: 'Không cần kích hoạt tài khoản cho phiên xác minh này.',
};

const UNKNOWN_ACTIVATION_MESSAGE = 'Quá trình kích hoạt tài khoản đã được ghi nhận.';

/**
 * Trình hướng dẫn xác thực danh tính điện tử (eKYC).
 *
 * Phán quyết do backend quyết định qua `finalizeEkycSession`; component không diễn giải
 * kết quả SDK. Widget VNPT được gắn đúng một lần cho mỗi phiên, sau khi div chứa đã có
 * trong DOM.
 */
export function EkycCaptureWizard({
  consentVersion,
  consentText,
  onVerified,
  onCancel,
}: EkycCaptureWizardProps) {
  const {
    stage,
    sessionId,
    proxyBasePath,
    finalizeResult,
    failureCode,
    failureReason,
    errorMessage,
    acceptConsentAndStart,
    handleSdkEndFlow,
    reportError,
    retry,
  } = useEkycFlow();

  const [consentAccepted, setConsentAccepted] = useState(false);
  const launchedSessionRef = useRef<string | null>(null);

  useEffect(() => {
    if (stage !== 'capturing' || sessionId === null || proxyBasePath === null) return;
    if (launchedSessionRef.current === sessionId) return;

    let active = true;

    void (async () => {
      try {
        const sdk = await loadVnptEkycSdk();
        // Div chứa đã render trước effect này; chỉ launch khi effect còn hiệu lực.
        if (!active || launchedSessionRef.current === sessionId) return;
        launchedSessionRef.current = sessionId;
        sdk.launch(
          buildEkycSdkConfig({
            proxyBasePath,
            onEndFlow: handleSdkEndFlow,
          }),
        );
      } catch {
        if (!active) return;
        reportError(SDK_LOAD_ERROR);
      }
    })();

    return () => {
      active = false;
    };
  }, [stage, sessionId, proxyBasePath, handleSdkEndFlow, reportError]);

  const handleRetry = useCallback(() => {
    setConsentAccepted(false);
    retry();
  }, [retry]);

  if (stage === 'consent' || stage === 'creating-session') {
    return (
      <EkycConsentScreen
        consentVersion={consentVersion}
        consentText={consentText}
        accepted={consentAccepted}
        onAcceptedChange={setConsentAccepted}
        onConfirm={() => {
          void acceptConsentAndStart(consentVersion);
        }}
        onCancel={onCancel}
        submitting={stage === 'creating-session'}
      />
    );
  }

  if (stage === 'capturing') {
    return (
      <Card className="mx-auto w-full max-w-3xl" role="region" aria-label="Xác minh danh tính">
        <CardHeader>
          <h2 className="text-base font-bold text-ink">Chụp giấy tờ tùy thân và khuôn mặt</h2>
          <p className="mt-1 text-sm text-sub">
            Thực hiện lần lượt theo hướng dẫn trên màn hình để hoàn tất xác minh.
          </p>
          <ul className="mt-3 list-disc space-y-1 pl-5 text-sm text-sub">
            <li>Đặt giấy tờ trên mặt phẳng, đủ ánh sáng và không bị lóa.</li>
            <li>Giữ khuôn mặt trong khung hình, không đeo khẩu trang hay kính râm.</li>
            <li>Không che khuất thông tin và không dùng ảnh chụp lại.</li>
          </ul>
        </CardHeader>
        <CardContent>
          <div id={EKYC_SDK_CONTAINER_ID} className="min-h-[520px] w-full" />
          <p className="mt-3 text-xs text-sub">
            Nếu trình xác minh không hiển thị, vui lòng kiểm tra quyền truy cập camera và tải lại
            trang.
          </p>
        </CardContent>
      </Card>
    );
  }

  if (stage === 'finalizing') {
    return (
      <Card className="mx-auto w-full max-w-xl">
        <CardContent className="flex flex-col items-center gap-3 py-12 text-center">
          <Spinner className="h-8 w-8 text-primary" />
          <p role="status" aria-live="polite" className="text-sm font-semibold text-ink">
            Đang xác minh…
          </p>
          <p className="max-w-sm text-xs text-sub">
            Vui lòng không đóng trang. Hệ thống đang đối chiếu giấy tờ và khuôn mặt của bạn.
          </p>
        </CardContent>
      </Card>
    );
  }

  if (stage === 'verified') {
    const activationMessage = finalizeResult?.activationOutcome
      ? (ACTIVATION_OUTCOME_MESSAGES[finalizeResult.activationOutcome] ??
        UNKNOWN_ACTIVATION_MESSAGE)
      : null;

    return (
      <Card className="mx-auto w-full max-w-xl" role="region" aria-label="Kết quả xác minh">
        <CardContent className="py-10 text-center">
          <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-emerald-50">
            <CheckCircle2 className="h-7 w-7 text-emerald-600" aria-hidden="true" />
          </div>
          <h2 className="mt-4 text-lg font-bold text-ink">Xác minh danh tính thành công</h2>
          <p className="mt-1 text-sm text-sub">
            Giấy tờ và khuôn mặt của bạn đã được xác minh.
          </p>

          {finalizeResult?.accountActivated === true ? (
            <Alert tone="success" className="mt-4 text-left">
              Tài khoản của bạn đã được kích hoạt.
            </Alert>
          ) : null}

          {finalizeResult?.accountActivated === false ? (
            <Alert tone="warning" className="mt-4 text-left">
              Xác minh thành công nhưng tài khoản chưa được kích hoạt. Vui lòng liên hệ bộ phận tiếp
              nhận để được hỗ trợ.
            </Alert>
          ) : null}

          {activationMessage ? <p className="mt-3 text-sm text-sub">{activationMessage}</p> : null}
        </CardContent>
        <CardFooter className="flex justify-end">
          <Button
            type="button"
            onClick={() => {
              if (finalizeResult) onVerified?.(finalizeResult);
            }}
          >
            Tiếp tục
          </Button>
        </CardFooter>
      </Card>
    );
  }

  if (stage === 'rejected') {
    const message = getEkycFailureMessage(failureCode, failureReason ?? undefined);
    const canRetry = isRetriableFailure(failureCode);

    return (
      <Card className="mx-auto w-full max-w-xl" role="region" aria-label="Kết quả xác minh">
        <CardHeader>
          <h2 className="text-base font-bold text-ink">Chưa thể xác minh danh tính</h2>
        </CardHeader>
        <CardContent className="space-y-4">
          <div role="alert">
            <Alert tone="error">{message}</Alert>
          </div>
          <p className="text-sm text-sub">
            {canRetry
              ? 'Bạn có thể thử lại với giấy tờ và điều kiện ánh sáng tốt hơn.'
              : 'Vui lòng liên hệ bộ phận tiếp nhận hoặc bộ phận hỗ trợ để được xác minh trực tiếp.'}
          </p>
        </CardContent>
        <CardFooter className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          {onCancel ? (
            <Button type="button" variant="secondary" onClick={onCancel}>
              Quay lại
            </Button>
          ) : null}
          {canRetry ? (
            <Button type="button" onClick={handleRetry}>
              Thử lại
            </Button>
          ) : null}
        </CardFooter>
      </Card>
    );
  }

  return (
    <Card className="mx-auto w-full max-w-xl" role="region" aria-label="Lỗi xác minh">
      <CardHeader>
        <h2 className="text-base font-bold text-ink">Không thể tiếp tục xác minh</h2>
      </CardHeader>
      <CardContent className="space-y-4">
        <div role="alert">
          <Alert tone="error">
            {errorMessage ?? 'Đã có lỗi xảy ra trong quá trình xác minh. Vui lòng thử lại.'}
          </Alert>
        </div>
        <p className="text-sm text-sub">
          Nếu lỗi tiếp tục lặp lại, vui lòng liên hệ bộ phận hỗ trợ để được trợ giúp.
        </p>
      </CardContent>
      <CardFooter className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
        {onCancel ? (
          <Button type="button" variant="secondary" onClick={onCancel}>
            Quay lại
          </Button>
        ) : null}
        <Button type="button" onClick={handleRetry}>
          Thử lại
        </Button>
      </CardFooter>
    </Card>
  );
}
