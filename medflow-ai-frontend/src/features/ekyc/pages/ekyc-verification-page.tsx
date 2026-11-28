import { useCallback, useEffect, useRef, useState } from 'react';
import { useNavigate } from 'react-router-dom';
import { CheckCircle2, Clock, LogIn, ShieldAlert, ShieldCheck } from 'lucide-react';

import { ACCESS_TOKEN_KEY, REFRESH_TOKEN_KEY } from '@/shared/lib/auth-storage';
import { Alert, Button, Card, CardContent, CardFooter, CardHeader, Spinner } from '@/shared/ui';

import { EkycConsentScreen } from '../components/ekyc-consent-screen';
import { useEkycFlow } from '../hooks/use-ekyc-flow';
import { EKYC_SDK_CONTAINER_ID, buildEkycSdkConfig } from '../sdk/ekyc-sdk-config';
import { loadVnptEkycSdk } from '../sdk/vnpt-sdk-loader';
import type { FinalizeEkycSessionResponse } from '../types/ekyc.types';
import { getEkycFailureMessage, isRetriableFailure } from '../utils/ekyc-errors';

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
  /** Ghi đè hành vi khi backend trả kết quả xác minh; mặc định về trang đăng nhập. */
  onVerified?: (result: FinalizeEkycSessionResponse) => void;
  /** Ghi đè hành vi khi thoát; mặc định xoá token đăng ký và về trang đăng nhập. */
  onCancel?: () => void;
}

const SDK_LOAD_ERROR =
  'Không thể tải trình xác minh danh tính. Vui lòng kiểm tra kết nối mạng và thử lại.';

type EkycOutcomeKind = 'activated' | 'conflict' | 'pending-review' | 'not-activated';

/**
 * Phân loại kết quả finalize theo ma trận dedupe của backend.
 * `CONFLICT_ACCOUNT_EXISTS` KHÔNG phải thành công dù phiên đã VERIFIED.
 */
function resolveOutcomeKind(result: FinalizeEkycSessionResponse): EkycOutcomeKind {
  const outcome = result.activationOutcome;

  if (outcome === 'CONFLICT_ACCOUNT_EXISTS') return 'conflict';

  if (
    outcome === 'FAMILY_MANAGED_PENDING_REVIEW' ||
    outcome === 'INFO_MISMATCH_PENDING_REVIEW' ||
    outcome === 'MULTIPLE_RECORDS_CONFLICT'
  ) {
    return 'pending-review';
  }

  if (result.accountActivated === true) {
    return 'activated';
  }

  return 'not-activated';
}

/**
 * Xoá token của phạm vi đăng ký khi rời luồng.
 * Chỉ xoá access token đơn lẻ (không có refresh token đi kèm) để không đụng vào phiên đăng nhập
 * đầy đủ của người dùng.
 */
function clearRegistrationScopeToken() {
  if (typeof localStorage === 'undefined') return;
  if (localStorage.getItem(REFRESH_TOKEN_KEY)) return;
  localStorage.removeItem(ACCESS_TOKEN_KEY);
}

function hasRegistrationScopeToken(): boolean {
  if (typeof localStorage === 'undefined') return false;
  return Boolean(localStorage.getItem(ACCESS_TOKEN_KEY));
}

/**
 * Luồng xác minh danh tính (eKYC) cho tài khoản đăng ký đang PENDING.
 *
 * Tài khoản chưa được kích hoạt nên chưa có phiên đăng nhập đầy đủ: người dùng chỉ có
 * registration-scope access token do `/auth/verify-otp` (hoặc lần đăng nhập resume) cấp.
 * Hoàn tất finalize KHÔNG cấp token đăng nhập — người dùng được hướng dẫn đăng nhập lại.
 */
export function EkycVerificationPage({ onVerified, onCancel }: EkycVerificationPageProps = {}) {
  const navigate = useNavigate();
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
  const [identityCard, setIdentityCard] = useState('');
  const [identityCardTouched, setIdentityCardTouched] = useState(false);
  const [tokenPresent] = useState(hasRegistrationScopeToken);
  const launchedSessionRef = useRef<string | null>(null);

  const isIdentityCardValid = identityCard.length === 9 || identityCard.length === 12;
  const showIdentityCardError = identityCardTouched && !isIdentityCardValid;

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

  useEffect(() => {
    // Finalize đã xong và không còn lời gọi eKYC nào cần token đăng ký → dọn token phạm vi đăng ký.
    if (stage === 'verified') {
      clearRegistrationScopeToken();
    }
  }, [stage]);

  const handleRetry = useCallback(() => {
    setConsentAccepted(false);
    retry();
  }, [retry]);

  const handleExit = useCallback(() => {
    if (onCancel) {
      onCancel();
      return;
    }
    clearRegistrationScopeToken();
    navigate('/login', { replace: true });
  }, [navigate, onCancel]);

  const handleOutcomeContinue = useCallback(() => {
    if (!finalizeResult) return;
    if (onVerified) {
      onVerified(finalizeResult);
      return;
    }
    clearRegistrationScopeToken();
    navigate('/login', { replace: true });
  }, [navigate, finalizeResult, onVerified]);

  const renderBody = () => {
    if (!tokenPresent) {
      return (
        <Card className="mx-auto w-full max-w-xl" role="region" aria-label="Phiên đăng ký">
          <CardHeader>
            <h2 className="text-base font-bold text-ink">Không tìm thấy phiên đăng ký</h2>
          </CardHeader>
          <CardContent className="space-y-4">
            <Alert tone="warning">
              Phiên xác thực email không còn hiệu lực. Vui lòng đăng nhập bằng tài khoản vừa đăng ký
              để tiếp tục xác minh danh tính.
            </Alert>
          </CardContent>
          <CardFooter className="flex justify-end">
            <Button type="button" onClick={() => navigate('/login', { replace: true })}>
              <LogIn className="h-4 w-4" aria-hidden="true" />
              Về trang đăng nhập
            </Button>
          </CardFooter>
        </Card>
      );
    }

    if (stage === 'consent' || stage === 'creating-session') {
      const isCreating = stage === 'creating-session';

      return (
        <>
          <Card className="mx-auto mb-4 w-full max-w-2xl">
            <CardHeader>
              <h2 className="text-base font-bold text-ink">Số giấy tờ tùy thân</h2>
              <p className="mt-1 text-sm text-sub">
                Nhập đúng số căn cước công dân (12 số) hoặc chứng minh nhân dân (9 số) in trên giấy
                tờ. Số này được đối chiếu với ảnh chụp ở bước tiếp theo.
              </p>
            </CardHeader>
            <CardContent>
              <label
                htmlFor="ekyc-identity-card"
                className="text-xs font-semibold uppercase tracking-wide text-sub"
              >
                Số CCCD/CMND
              </label>
              <input
                id="ekyc-identity-card"
                type="text"
                inputMode="numeric"
                autoComplete="off"
                value={identityCard}
                disabled={isCreating}
                placeholder="Ví dụ: 012345678901"
                aria-invalid={showIdentityCardError}
                aria-describedby={showIdentityCardError ? 'ekyc-identity-card-error' : undefined}
                onChange={(event) => {
                  setIdentityCard(event.target.value.replace(/\D/g, '').slice(0, 12));
                }}
                className="mt-1 w-full rounded-lg border border-border bg-white px-3 py-2 font-mono text-sm tracking-wide text-ink focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 disabled:cursor-not-allowed disabled:bg-canvas"
              />
              {showIdentityCardError ? (
                <p id="ekyc-identity-card-error" className="mt-1.5 text-xs text-rose-600">
                  Số giấy tờ phải gồm 12 chữ số (CCCD) hoặc 9 chữ số (CMND).
                </p>
              ) : null}
            </CardContent>
          </Card>

          <EkycConsentScreen
            consentVersion={EKYC_CONSENT_VERSION}
            consentText={EKYC_CONSENT_TEXT_PLACEHOLDER}
            accepted={consentAccepted}
            onAcceptedChange={setConsentAccepted}
            onConfirm={() => {
              setIdentityCardTouched(true);
              if (!isIdentityCardValid) return;
              void acceptConsentAndStart(EKYC_CONSENT_VERSION, identityCard);
            }}
            onCancel={handleExit}
            submitting={isCreating}
          />
        </>
      );
    }

    if (stage === 'capturing') {
      return (
        <Card className="mx-auto w-full max-w-3xl" role="region" aria-label="Xác minh danh tính">
          <CardHeader>
            <h2 className="text-base font-bold text-ink">Chụp giấy tờ tùy thân</h2>
            <p className="mt-1 text-sm text-sub">
              Thực hiện lần lượt theo hướng dẫn trên màn hình để hoàn tất xác minh.
            </p>
            <ul className="mt-3 list-disc space-y-1 pl-5 text-sm text-sub">
              <li>Đặt giấy tờ trên mặt phẳng, đủ ánh sáng và không bị lóa.</li>
              <li>Chụp rõ mặt trước và mặt sau của căn cước công dân.</li>
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
              Vui lòng không đóng trang. Hệ thống đang đối chiếu giấy tờ của bạn.
            </p>
          </CardContent>
        </Card>
      );
    }

    if (stage === 'verified' && finalizeResult) {
      const kind = resolveOutcomeKind(finalizeResult);
      const reviewReason = finalizeResult.activationReviewReason?.trim();

      if (kind === 'conflict') {
        return (
          <Card className="mx-auto w-full max-w-xl" role="region" aria-label="Kết quả xác minh">
            <CardContent className="py-10 text-center">
              <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-rose-50">
                <ShieldAlert className="h-7 w-7 text-rose-600" aria-hidden="true" />
              </div>
              <h2 className="mt-4 text-lg font-bold text-ink">Không thể kích hoạt tài khoản</h2>
              <p className="mt-1 text-sm text-sub">
                Giấy tờ của bạn đã được xác minh nhưng số định danh này đang gắn với một tài khoản
                khác.
              </p>
              <Alert tone="warning" className="mt-4 text-left">
                Vui lòng sử dụng chức năng quên mật khẩu để khôi phục tài khoản hiện có, hoặc liên
                hệ quầy tiếp nhận để được hỗ trợ. Không tạo thêm tài khoản mới với cùng giấy tờ.
              </Alert>
              {reviewReason ? <p className="mt-3 text-sm text-sub">{reviewReason}</p> : null}
            </CardContent>
            <CardFooter className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
              <Button
                type="button"
                variant="secondary"
                onClick={() => navigate('/forgot-password', { replace: true })}
              >
                Quên mật khẩu
              </Button>
              <Button type="button" onClick={handleOutcomeContinue}>
                Về trang đăng nhập
              </Button>
            </CardFooter>
          </Card>
        );
      }

      if (kind === 'pending-review') {
        return (
          <Card className="mx-auto w-full max-w-xl" role="region" aria-label="Kết quả xác minh">
            <CardContent className="py-10 text-center">
              <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-amber-50">
                <Clock className="h-7 w-7 text-amber-600" aria-hidden="true" />
              </div>
              <h2 className="mt-4 text-lg font-bold text-ink">Đã xác minh danh tính</h2>
              <p className="mt-1 text-sm text-sub">
                {finalizeResult.accountActivated === true
                  ? 'Tài khoản của bạn đã được kích hoạt. Việc liên kết hồ sơ bệnh nhân cần bộ phận tiếp nhận xem xét thêm.'
                  : 'Danh tính đã được xác minh nhưng tài khoản chưa được kích hoạt tự động.'}
              </p>
              <Alert tone="warning" className="mt-4 text-left">
                {reviewReason ??
                  'Bộ phận tiếp nhận sẽ xem xét và liên kết hồ sơ bệnh nhân của bạn trong thời gian sớm nhất.'}
              </Alert>
              {finalizeResult.accountActivated === true ? (
                <p className="mt-3 text-sm text-sub">
                  Bạn có thể đăng nhập và sử dụng tài khoản; việc liên kết hồ sơ sẽ hoàn tất sau khi
                  được duyệt.
                </p>
              ) : (
                <p className="mt-3 text-sm text-sub">
                  Vui lòng liên hệ quầy tiếp nhận để được hỗ trợ kích hoạt tài khoản.
                </p>
              )}
            </CardContent>
            <CardFooter className="flex justify-end">
              <Button type="button" onClick={handleOutcomeContinue}>
                {finalizeResult.accountActivated === true ? 'Đăng nhập' : 'Về trang đăng nhập'}
              </Button>
            </CardFooter>
          </Card>
        );
      }

      if (kind === 'activated') {
        return (
          <Card className="mx-auto w-full max-w-xl" role="region" aria-label="Kết quả xác minh">
            <CardContent className="py-10 text-center">
              <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-emerald-50">
                <CheckCircle2 className="h-7 w-7 text-emerald-600" aria-hidden="true" />
              </div>
              <h2 className="mt-4 text-lg font-bold text-ink">Tài khoản đã được kích hoạt</h2>
              <p className="mt-1 text-sm text-sub">
                {finalizeResult.activationOutcome === 'CLAIM_LINKED_ACTIVATED'
                  ? 'Danh tính của bạn đã được xác minh và liên kết với hồ sơ bệnh nhân đã có.'
                  : 'Danh tính của bạn đã được xác minh và ghi nhận cho hồ sơ bệnh nhân.'}
              </p>
              <Alert tone="success" className="mt-4 text-left">
                Vui lòng đăng nhập lại bằng email và mật khẩu đã đăng ký để sử dụng tài khoản.
              </Alert>
            </CardContent>
            <CardFooter className="flex justify-end">
              <Button type="button" onClick={handleOutcomeContinue}>
                <LogIn className="h-4 w-4" aria-hidden="true" />
                Đăng nhập
              </Button>
            </CardFooter>
          </Card>
        );
      }

      return (
        <Card className="mx-auto w-full max-w-xl" role="region" aria-label="Kết quả xác minh">
          <CardContent className="py-10 text-center">
            <div className="mx-auto flex h-14 w-14 items-center justify-center rounded-full bg-amber-50">
              <ShieldCheck className="h-7 w-7 text-amber-600" aria-hidden="true" />
            </div>
            <h2 className="mt-4 text-lg font-bold text-ink">Đã xác minh danh tính</h2>
            <p className="mt-1 text-sm text-sub">
              Hệ thống đã ghi nhận kết quả xác minh giấy tờ của bạn.
            </p>
            <Alert tone="warning" className="mt-4 text-left">
              Tài khoản chưa được kích hoạt tự động. Vui lòng liên hệ quầy tiếp nhận để được hỗ trợ.
            </Alert>
            {reviewReason ? <p className="mt-3 text-sm text-sub">{reviewReason}</p> : null}
          </CardContent>
          <CardFooter className="flex justify-end">
            <Button type="button" onClick={handleOutcomeContinue}>
              Về trang đăng nhập
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
            <Button type="button" variant="secondary" onClick={handleExit}>
              Về trang đăng nhập
            </Button>
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
          <Button type="button" variant="secondary" onClick={handleExit}>
            Về trang đăng nhập
          </Button>
          <Button type="button" onClick={handleRetry}>
            Thử lại
          </Button>
        </CardFooter>
      </Card>
    );
  };

  return (
    <main className="mx-auto w-full max-w-4xl px-4 py-8 sm:px-6">
      <header className="mb-6">
        <h1 className="text-xl font-bold text-ink sm:text-2xl">Xác minh danh tính điện tử</h1>
        <p className="mt-1 text-sm text-sub">
          Tài khoản của bạn đã được xác thực email. Hoàn tất xác minh giấy tờ tùy thân để kích hoạt
          tài khoản bệnh nhân.
        </p>
      </header>

      {renderBody()}
    </main>
  );
}
