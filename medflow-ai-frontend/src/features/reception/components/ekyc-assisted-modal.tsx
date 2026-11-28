import { useCallback, useEffect, useRef, useState } from 'react';

import {
  Alert,
  Badge,
  Button,
  Card,
  CardContent,
  CardFooter,
  CardHeader,
  Input,
  Spinner,
} from '@/shared/ui';
import { Modal } from '@/shared/ui/modal';
import { getErrorMessage } from '@/shared/lib/get-error-message';

import { EkycCaptureStep } from '@/features/ekyc/components/ekyc-capture-step';
import { EkycConsentScreen } from '@/features/ekyc/components/ekyc-consent-screen';
import { createReceptionEkycSession, finalizeEkycSession } from '@/features/ekyc/api/ekyc.api';
import {
  EKYC_CONSENT_TEXT_PLACEHOLDER,
  EKYC_CONSENT_VERSION,
} from '@/features/ekyc/pages/ekyc-verification-page';
import type { EkycFlowStage } from '@/features/ekyc/hooks/use-ekyc-flow';
import type { FinalizeEkycSessionResponse } from '@/features/ekyc/types/ekyc.types';
import type { VnptEkycResult } from '@/features/ekyc/sdk/vnpt-sdk.types';
import { getEkycFailureMessage, isRetriableFailure } from '@/features/ekyc/utils/ekyc-errors';

import { PATIENT_IDENTITY_STATUS_LABELS } from '../types/patient-identity.types';

export interface EkycAssistedModalProps {
  /** Id hồ sơ bệnh nhân (số) — backend gắn phiên xác minh với hồ sơ này. */
  patientId: number;
  patientName: string;
  open: boolean;
  onClose: () => void;
  /** Được gọi ngay khi backend kết luận hồ sơ đã xác minh (để trang cha tải lại). */
  onVerified?: (result: FinalizeEkycSessionResponse) => void;
}

const CREATE_SESSION_ERROR =
  'Không thể tạo phiên xác minh cho hồ sơ bệnh nhân. Vui lòng thử lại.';
const FINALIZE_ERROR = 'Không thể hoàn tất xác minh danh tính. Vui lòng thử lại.';
const IDENTITY_CARD_REGEX = /^(?:\d{9}|\d{12})$/;
const IDENTITY_CARD_REQUIRED_ERROR =
  'Vui lòng nhập đúng 9 hoặc 12 chữ số CCCD/VNeID trên giấy tờ của bệnh nhân.';

interface CaptureSession {
  sessionId: string;
  proxyBasePath: string;
}

/**
 * Xác minh danh tính điện tử do lễ tân hỗ trợ cho một hồ sơ bệnh nhân.
 *
 * Dùng chung engine chụp với luồng tự phục vụ (EkycCaptureStep) nhưng tạo phiên qua
 * endpoint reception và gắn phiên với `patientId`. Phán quyết chỉ đến từ finalize —
 * component không diễn giải kết quả SDK.
 */
export function EkycAssistedModal({
  patientId,
  patientName,
  open,
  onClose,
  onVerified,
}: EkycAssistedModalProps) {
  const [stage, setStage] = useState<EkycFlowStage>('consent');
  const [session, setSession] = useState<CaptureSession | null>(null);
  const [consentAccepted, setConsentAccepted] = useState(false);
  /** Số CCCD/VNeID trên giấy tờ của bệnh nhân — backend bắt buộc khai báo định danh. */
  const [identityCard, setIdentityCard] = useState('');
  const [finalizeResult, setFinalizeResult] = useState<FinalizeEkycSessionResponse | null>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const generationRef = useRef(0);
  const sessionIdRef = useRef<string | null>(null);
  const finalizingRef = useRef(false);
  const verifiedNotifiedRef = useRef(false);

  const reset = useCallback(() => {
    generationRef.current += 1;
    sessionIdRef.current = null;
    finalizingRef.current = false;
    verifiedNotifiedRef.current = false;
    setStage('consent');
    setSession(null);
    setConsentAccepted(false);
    setIdentityCard('');
    setFinalizeResult(null);
    setErrorMessage(null);
  }, []);

  // Mỗi lần mở lại (hoặc đổi bệnh nhân) bắt đầu từ bước đồng ý với phiên mới.
  useEffect(() => {
    if (!open) return;
    reset();
  }, [open, patientId, reset]);

  const handleClose = () => {
    reset();
    onClose();
  };

  const handleConfirmConsent = async () => {
    if (stage === 'creating-session') return;

    const trimmedCard = identityCard.trim();
    if (!IDENTITY_CARD_REGEX.test(trimmedCard)) {
      setErrorMessage(IDENTITY_CARD_REQUIRED_ERROR);
      return;
    }

    const generation = generationRef.current + 1;
    generationRef.current = generation;
    setStage('creating-session');
    setErrorMessage(null);

    try {
      const created = await createReceptionEkycSession(patientId, {
        consentVersion: EKYC_CONSENT_VERSION,
        consentAccepted: true,
        identityCard: trimmedCard,
      });
      if (generationRef.current !== generation) return;

      sessionIdRef.current = created.sessionId;
      setSession({ sessionId: created.sessionId, proxyBasePath: created.proxyBasePath });
      setStage('capturing');
    } catch (error) {
      if (generationRef.current !== generation) return;
      setStage('error');
      setErrorMessage(getErrorMessage(error, CREATE_SESSION_ERROR));
    }
  };

  const handleEndFlow = useCallback(
    (_result: VnptEkycResult) => {
      // Bằng chứng SDK là dữ liệu mờ — chỉ coi đây là tín hiệu "đã chụp xong".
      if (finalizingRef.current) return;
      const currentSessionId = sessionIdRef.current;
      if (currentSessionId === null) return;

      finalizingRef.current = true;
      const generation = generationRef.current;
      setStage('finalizing');

      void (async () => {
        try {
          const finalized = await finalizeEkycSession(currentSessionId);
          if (generationRef.current !== generation) return;

          setFinalizeResult(finalized);
          if (
            finalized.status === 'VERIFIED' ||
            finalized.patientIdentityStatus === 'VERIFIED'
          ) {
            setStage('verified');
            if (!verifiedNotifiedRef.current) {
              verifiedNotifiedRef.current = true;
              onVerified?.(finalized);
            }
            return;
          }
          setStage('rejected');
        } catch (error) {
          if (generationRef.current !== generation) return;
          setStage('error');
          setErrorMessage(getErrorMessage(error, FINALIZE_ERROR));
        } finally {
          finalizingRef.current = false;
        }
      })();
    },
    [onVerified],
  );

  const handleSdkLoadError = useCallback((message: string) => {
    sessionIdRef.current = null;
    finalizingRef.current = false;
    setStage('error');
    setErrorMessage(message);
  }, []);

  const verifiedIdentityStatus = finalizeResult?.patientIdentityStatus ?? 'VERIFIED';

  return (
    <Modal open={open} onClose={handleClose} maxWidth="max-w-3xl">
      <div className="max-h-[90vh] overflow-y-auto" aria-label="Xác minh danh tính hỗ trợ">
        <div className="border-b border-border px-6 py-4 pr-14">
          <h2 className="text-lg font-bold text-navy">Xác minh danh tính hỗ trợ</h2>
          <p className="text-xs text-sub">
            Hồ sơ bệnh nhân: <span className="font-semibold text-navy">{patientName}</span>
          </p>
        </div>

        <div className="p-6">
          {stage === 'consent' || stage === 'creating-session' ? (
            <div className="space-y-4">
              <Alert tone="info">
                Lễ tân hỗ trợ thao tác trên thiết bị tại quầy. Bệnh nhân cần đọc và tự xác nhận
                đồng ý trước khi chụp giấy tờ tùy thân.
              </Alert>
              <Input
                id="assisted-identity-card"
                label="Số CCCD/VNeID trên giấy tờ"
                inputMode="numeric"
                autoComplete="off"
                maxLength={12}
                value={identityCard}
                onChange={(event) =>
                  setIdentityCard(event.target.value.replace(/\D/g, '').slice(0, 12))
                }
                error={
                  errorMessage === IDENTITY_CARD_REQUIRED_ERROR ? errorMessage : undefined
                }
              />
              {errorMessage && errorMessage !== IDENTITY_CARD_REQUIRED_ERROR ? (
                <Alert tone="error">{errorMessage}</Alert>
              ) : null}
              <EkycConsentScreen
                consentVersion={EKYC_CONSENT_VERSION}
                consentText={EKYC_CONSENT_TEXT_PLACEHOLDER}
                accepted={consentAccepted}
                onAcceptedChange={setConsentAccepted}
                onConfirm={() => {
                  void handleConfirmConsent();
                }}
                onCancel={handleClose}
                submitting={stage === 'creating-session'}
              />
            </div>
          ) : null}

          {stage === 'capturing' && session ? (
            <Card className="mx-auto w-full max-w-2xl" role="region" aria-label="Chụp giấy tờ">
              <CardHeader>
                <h3 className="text-base font-bold text-ink">Chụp giấy tờ tùy thân</h3>
                <p className="mt-1 text-sm text-sub">
                  Thực hiện theo hướng dẫn trên màn hình để hoàn tất xác minh giấy tờ.
                </p>
                <ul className="mt-3 list-disc space-y-1 pl-5 text-sm text-sub">
                  <li>Đặt CCCD/VNeID trên mặt phẳng, đủ ánh sáng và không bị lóa.</li>
                  <li>Không che khuất thông tin và không dùng ảnh chụp lại.</li>
                  <li>Chụp lần lượt mặt trước và mặt sau theo hướng dẫn.</li>
                </ul>
              </CardHeader>
              <CardContent>
                <EkycCaptureStep
                  key={session.sessionId}
                  sessionId={session.sessionId}
                  proxyBasePath={session.proxyBasePath}
                  onEndFlow={handleEndFlow}
                  onLoadError={handleSdkLoadError}
                />
                <p className="mt-3 text-xs text-sub">
                  Nếu trình xác minh không hiển thị, vui lòng kiểm tra quyền truy cập camera và
                  thử lại.
                </p>
              </CardContent>
            </Card>
          ) : null}

          {stage === 'finalizing' ? (
            <Card className="mx-auto w-full max-w-xl">
              <CardContent className="flex flex-col items-center gap-3 py-12 text-center">
                <Spinner className="h-8 w-8 text-primary" />
                <p role="status" aria-live="polite" className="text-sm font-semibold text-ink">
                  Đang xác minh…
                </p>
                <p className="max-w-sm text-xs text-sub">
                  Vui lòng không đóng cửa sổ. Hệ thống đang đối chiếu giấy tờ đã chụp.
                </p>
              </CardContent>
            </Card>
          ) : null}

          {stage === 'verified' ? (
            <Card className="mx-auto w-full max-w-xl" role="region" aria-label="Kết quả xác minh">
              <CardHeader>
                <h3 className="text-base font-bold text-ink">Đã xác minh danh tính bệnh nhân</h3>
              </CardHeader>
              <CardContent className="space-y-3">
                <div className="flex flex-wrap items-center gap-2 text-sm text-navy">
                  <span className="font-semibold">{patientName}</span>
                  <Badge tone={verifiedIdentityStatus === 'VERIFIED' ? 'emerald' : 'slate'}>
                    {PATIENT_IDENTITY_STATUS_LABELS[verifiedIdentityStatus]}
                  </Badge>
                </div>
                <p className="text-sm text-sub">
                  Hồ sơ bệnh nhân #{finalizeResult?.patientId ?? patientId} đã được ghi nhận trạng
                  thái xác minh danh tính.
                </p>
                <Alert tone="success">
                  Giấy tờ tùy thân của bệnh nhân đã được xác minh thành công.
                </Alert>
              </CardContent>
              <CardFooter className="flex justify-end">
                <Button type="button" onClick={handleClose}>
                  Hoàn tất
                </Button>
              </CardFooter>
            </Card>
          ) : null}

          {stage === 'rejected' ? (
            <Card className="mx-auto w-full max-w-xl" role="region" aria-label="Kết quả xác minh">
              <CardHeader>
                <h3 className="text-base font-bold text-ink">Chưa thể xác minh danh tính</h3>
              </CardHeader>
              <CardContent className="space-y-4">
                <div role="alert">
                  <Alert tone="error">
                    {getEkycFailureMessage(
                      finalizeResult?.failureCode,
                      finalizeResult?.failureReason ?? undefined,
                    )}
                  </Alert>
                </div>
                <p className="text-sm text-sub">
                  {isRetriableFailure(finalizeResult?.failureCode)
                    ? 'Có thể thử lại với giấy tờ và điều kiện ánh sáng tốt hơn.'
                    : 'Vui lòng kiểm tra lại giấy tờ hoặc liên hệ bộ phận hỗ trợ để được xác minh trực tiếp.'}
                </p>
              </CardContent>
              <CardFooter className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
                <Button type="button" variant="secondary" onClick={handleClose}>
                  Đóng
                </Button>
                {isRetriableFailure(finalizeResult?.failureCode) ? (
                  <Button type="button" onClick={reset}>
                    Thử lại
                  </Button>
                ) : null}
              </CardFooter>
            </Card>
          ) : null}

          {stage === 'error' ? (
            <Card className="mx-auto w-full max-w-xl" role="region" aria-label="Lỗi xác minh">
              <CardHeader>
                <h3 className="text-base font-bold text-ink">Không thể tiếp tục xác minh</h3>
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
                <Button type="button" variant="secondary" onClick={handleClose}>
                  Đóng
                </Button>
                <Button type="button" onClick={reset}>
                  Thử lại
                </Button>
              </CardFooter>
            </Card>
          ) : null}
        </div>
      </div>
    </Modal>
  );
}
