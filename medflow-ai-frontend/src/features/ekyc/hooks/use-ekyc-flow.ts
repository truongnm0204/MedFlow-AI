import { useCallback, useEffect, useRef, useState } from 'react';
import { AxiosError } from 'axios';

import { getErrorMessage } from '@/shared/lib/get-error-message';

import type { FinalizeEkycSessionResponse, EkycFailureCode } from '../types/ekyc.types';
import type { VnptEkycResult } from '../sdk/vnpt-sdk.types';
import { createEkycSession, finalizeEkycSession } from '../api/ekyc.api';

/**
 * Machine-readable stages of the eKYC flow.
 *
 * `capturing` means the VNPT SDK is mounted and the user is photographing documents/face.
 * `finalizing` means the SDK reported end-of-flow and the backend is deciding the verdict.
 * The verdict is NEVER derived from the SDK result object: it comes only from the finalize
 * endpoint response.
 */
export type EkycFlowStage =
  | 'consent'
  | 'creating-session'
  | 'capturing'
  | 'finalizing'
  | 'verified'
  | 'rejected'
  | 'error';

interface EkycFlowState {
  stage: EkycFlowStage;
  sessionId: string | null;
  proxyBasePath: string | null;
  finalizeResult: FinalizeEkycSessionResponse | null;
  errorMessage: string | null;
}

export interface UseEkycFlowResult {
  stage: EkycFlowStage;
  sessionId: string | null;
  proxyBasePath: string | null;
  finalizeResult: FinalizeEkycSessionResponse | null;
  failureCode: EkycFailureCode | null;
  failureReason: string | null;
  errorMessage: string | null;

  isConsentStage: boolean;
  isCreatingSession: boolean;
  isCapturing: boolean;
  isFinalizing: boolean;
  isVerified: boolean;
  isRejected: boolean;
  isError: boolean;
  /** Đang có thao tác mạng chạy (tạo phiên hoặc hoàn tất xác minh). */
  isBusy: boolean;

  /** Bắt đầu phiên mới sau khi người dùng đã đồng ý. */
  acceptConsentAndStart: (consentVersion: string) => Promise<void>;
  /** SDK báo kết thúc chụp — điểm duy nhất kích hoạt finalize. */
  handleSdkEndFlow: (result: VnptEkycResult) => void;
  /** Báo lỗi hạ tầng SDK (tải/khởi động widget) để vào stage 'error'. */
  reportError: (message: string) => void;
  /** Quay về bước đồng ý để bắt đầu phiên mới (không tái sử dụng phiên đã kết thúc). */
  retry: () => void;
  /** Đặt lại toàn bộ trạng thái về ban đầu. */
  reset: () => void;
}

const INITIAL_STATE: EkycFlowState = {
  stage: 'consent',
  sessionId: null,
  proxyBasePath: null,
  finalizeResult: null,
  errorMessage: null,
};

const CREATE_SESSION_ERROR = 'Không thể tạo phiên xác minh. Vui lòng thử lại.';
const FINALIZE_ERROR = 'Không thể hoàn tất xác minh danh tính. Vui lòng thử lại.';
/** 409 khi finalize nghĩa là một tab/người khác đang hoàn tất cùng phiên. */
const FINALIZE_CONFLICT_ERROR =
  'Phiên xác minh đang được xử lý đồng thời. Vui lòng thử lại để bắt đầu phiên mới.';

/**
 * Gọi finalize một lần; nếu gặp 409 (finalize đồng thời) thì thử lại đúng một lần.
 * Ném lại lỗi để phía gọi hiển thị thông báo phù hợp.
 */
async function finalizeWithConflictRetry(
  sessionId: string,
): Promise<FinalizeEkycSessionResponse> {
  try {
    return await finalizeEkycSession(sessionId);
  } catch (error) {
    if (!(error instanceof AxiosError) || error.response?.status !== 409) throw error;
    return await finalizeEkycSession(sessionId);
  }
}

/**
 * State machine của toàn bộ luồng eKYC.
 *
 * Không lưu trữ dữ liệu sinh trắc học hay kết quả SDK: `handleSdkEndFlow` chỉ dùng callback
 * của SDK như tín hiệu "đã chụp xong". Phán quyết lấy từ `finalizeEkycSession`.
 */
export function useEkycFlow(): UseEkycFlowResult {
  const [state, setState] = useState<EkycFlowState>(INITIAL_STATE);

  const mountedRef = useRef(true);
  const generationRef = useRef(0);
  const creatingRef = useRef(false);
  /** true khi đã tạo phiên và chưa kích hoạt finalize — chặn finalize trùng. */
  const capturingRef = useRef(false);
  const sessionIdRef = useRef<string | null>(null);

  useEffect(() => {
    mountedRef.current = true;
    return () => {
      mountedRef.current = false;
    };
  }, []);

  const reset = useCallback(() => {
    generationRef.current += 1;
    creatingRef.current = false;
    capturingRef.current = false;
    sessionIdRef.current = null;
    setState(INITIAL_STATE);
  }, []);

  const retry = useCallback(() => {
    // Phiên đã kết thúc (hoặc lỗi) không thể tái sử dụng — quay lại bước đồng ý để tạo phiên mới.
    reset();
  }, [reset]);

  const reportError = useCallback((message: string) => {
    capturingRef.current = false;
    setState((previous) => ({ ...previous, stage: 'error', errorMessage: message }));
  }, []);

  const acceptConsentAndStart = useCallback(async (consentVersion: string) => {
    if (creatingRef.current) return;

    const generation = generationRef.current + 1;
    generationRef.current = generation;
    creatingRef.current = true;
    capturingRef.current = false;
    sessionIdRef.current = null;

    setState({
      stage: 'creating-session',
      sessionId: null,
      proxyBasePath: null,
      finalizeResult: null,
      errorMessage: null,
    });

    try {
      const session = await createEkycSession({ consentVersion, consentAccepted: true });
      if (!mountedRef.current || generationRef.current !== generation) return;

      sessionIdRef.current = session.sessionId;
      capturingRef.current = true;
      setState({
        stage: 'capturing',
        sessionId: session.sessionId,
        proxyBasePath: session.proxyBasePath,
        finalizeResult: null,
        errorMessage: null,
      });
    } catch (error) {
      if (!mountedRef.current || generationRef.current !== generation) return;
      setState({
        stage: 'error',
        sessionId: null,
        proxyBasePath: null,
        finalizeResult: null,
        errorMessage: getErrorMessage(error, CREATE_SESSION_ERROR),
      });
    } finally {
      if (generationRef.current === generation) {
        creatingRef.current = false;
      }
    }
  }, []);

  const handleSdkEndFlow = useCallback((_result: VnptEkycResult) => {
    // Kết quả SDK là bằng chứng mờ (opaque) — chỉ dùng làm tín hiệu "đã chụp xong".
    if (!capturingRef.current) return;
    const currentSessionId = sessionIdRef.current;
    if (currentSessionId === null) return;

    capturingRef.current = false;
    const generation = generationRef.current;

    setState((previous) => ({ ...previous, stage: 'finalizing', errorMessage: null }));

    void (async () => {
      try {
        const finalizeResult = await finalizeWithConflictRetry(currentSessionId);
        if (!mountedRef.current || generationRef.current !== generation) return;

        if (finalizeResult.status === 'VERIFIED') {
          setState((previous) => ({
            ...previous,
            stage: 'verified',
            finalizeResult,
            errorMessage: null,
          }));
          return;
        }

        setState((previous) => ({
          ...previous,
          stage: 'rejected',
          finalizeResult,
          errorMessage: null,
        }));
      } catch (error) {
        if (!mountedRef.current || generationRef.current !== generation) return;
        setState((previous) => ({
          ...previous,
          stage: 'error',
          finalizeResult: null,
          errorMessage:
            error instanceof AxiosError && error.response?.status === 409
              ? FINALIZE_CONFLICT_ERROR
              : getErrorMessage(error, FINALIZE_ERROR),
        }));
      }
    })();
  }, []);

  const failureCode = state.finalizeResult?.failureCode ?? null;
  const failureReason = state.finalizeResult?.failureReason ?? null;

  return {
    stage: state.stage,
    sessionId: state.sessionId,
    proxyBasePath: state.proxyBasePath,
    finalizeResult: state.finalizeResult,
    failureCode,
    failureReason,
    errorMessage: state.errorMessage,

    isConsentStage: state.stage === 'consent',
    isCreatingSession: state.stage === 'creating-session',
    isCapturing: state.stage === 'capturing',
    isFinalizing: state.stage === 'finalizing',
    isVerified: state.stage === 'verified',
    isRejected: state.stage === 'rejected',
    isError: state.stage === 'error',
    isBusy: state.stage === 'creating-session' || state.stage === 'finalizing',

    acceptConsentAndStart,
    handleSdkEndFlow,
    reportError,
    retry,
    reset,
  };
}
