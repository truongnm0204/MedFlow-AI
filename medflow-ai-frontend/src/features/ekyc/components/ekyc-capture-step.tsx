import { useEffect, useRef } from 'react';

import { EKYC_SDK_CONTAINER_ID, buildEkycSdkConfig } from '../sdk/ekyc-sdk-config';
import { loadVnptEkycSdk } from '../sdk/vnpt-sdk-loader';
import type { VnptEkycResult } from '../sdk/vnpt-sdk.types';

/**
 * Bước chụp giấy tờ dùng chung: gắn widget VNPT đúng một lần cho mỗi phiên.
 *
 * Chỉ chịu trách nhiệm mount SDK và chuyển tín hiệu kết thúc lên component cha.
 * Phán quyết xác minh vẫn chỉ đến từ endpoint finalize — component này không diễn giải
 * kết quả SDK. Dùng được cho cả luồng tự phục vụ và luồng lễ tân hỗ trợ.
 */
export interface EkycCaptureStepProps {
  /** Mã phiên do backend cấp; đổi mã phiên sẽ mount lại widget. */
  sessionId: string;
  /** Đường dẫn proxy backend mà SDK dùng làm BACKEND_URL. */
  proxyBasePath: string;
  /** SDK báo kết thúc toàn bộ luồng chụp. Bằng chứng là dữ liệu mờ. */
  onEndFlow: (result: VnptEkycResult) => void;
  /** Không tải được SDK hoặc widget không khởi động được. */
  onLoadError: (message: string) => void;
}

const SDK_LOAD_ERROR =
  'Không thể tải trình xác minh danh tính. Vui lòng kiểm tra kết nối mạng và thử lại.';

export function EkycCaptureStep({
  sessionId,
  proxyBasePath,
  onEndFlow,
  onLoadError,
}: EkycCaptureStepProps) {
  const launchedSessionRef = useRef<string | null>(null);
  const callbacksRef = useRef({ onEndFlow, onLoadError });

  useEffect(() => {
    callbacksRef.current = { onEndFlow, onLoadError };
  }, [onEndFlow, onLoadError]);

  useEffect(() => {
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
            onEndFlow: (result) => callbacksRef.current.onEndFlow(result),
          }),
        );
      } catch {
        if (active) {
          callbacksRef.current.onLoadError(SDK_LOAD_ERROR);
        }
      }
    })();

    return () => {
      active = false;
    };
  }, [sessionId, proxyBasePath]);

  return <div id={EKYC_SDK_CONTAINER_ID} className="min-h-[520px] w-full" />;
}
