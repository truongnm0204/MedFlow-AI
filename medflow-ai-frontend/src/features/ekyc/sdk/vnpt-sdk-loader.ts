/**
 * Loader cho VNPT eKYC Web SDK (bundle 3.0).
 *
 * SDK không phải npm package mà là các script toàn cục đã vendor trong `public/ekyc-sdk/`.
 * Loader chèn tuần tự theo đúng thứ tự phụ thuộc:
 *   1. web-sdk-version-3.0.js  → gán `window.SDK`
 *   2. lib/VNPTQRBrowserApp.js
 *   3. lib/VNPTBrowserSDKAppV4.1.0.js → gán `window.FaceVNPTBrowserSDK`
 *
 * Chỉ resolve khi CẢ HAI global đã sẵn sàng. Không dùng CDN, không phụ thuộc mạng ngoài.
 * Lưu ý: trước khi gọi `window.SDK.launch(config)`, màn hình wizard phải tự tạo div
 * id `ekyc_sdk_intergrated` (đúng lỗi chính tả này — SDK hardcode id đó).
 */

import type { VnptEkycSdk } from './vnpt-sdk.types';

/** Đường dẫn gốc của các asset SDK đã vendor trong `public/ekyc-sdk`. */
const SDK_BASE_PATH = '/ekyc-sdk';

/** Thứ tự nạp bắt buộc: bundle chính trước, hai thư viện phụ thuộc sau. */
const SDK_SCRIPT_SOURCES = [
  `${SDK_BASE_PATH}/web-sdk-version-3.0.js`,
  `${SDK_BASE_PATH}/lib/VNPTQRBrowserApp.js`,
  `${SDK_BASE_PATH}/lib/VNPTBrowserSDKAppV4.1.0.js`,
] as const;

/** Đánh dấu script đã nạp xong, tránh chèn trùng hoặc chờ nhầm. */
const LOADED_ATTR = 'data-vnpt-ekyc-loaded';

/** Promise dùng chung để bảo đảm single-flight cho mọi lời gọi đồng thời. */
let sdkLoadPromise: Promise<VnptEkycSdk> | null = null;

function findExistingScript(src: string): HTMLScriptElement | null {
  const scripts = document.querySelectorAll<HTMLScriptElement>('script[src]');
  for (const script of Array.from(scripts)) {
    if (script.getAttribute('src') === src) {
      return script;
    }
  }
  return null;
}

/**
 * Chèn một script và resolve khi nạp xong.
 * Nếu script cùng `src` đã tồn tại (ví dụ HMR hoặc lời gọi trước đó) thì không chèn lại:
 * đã nạp → resolve ngay; đang nạp → chờ sự kiện load/error hiện có.
 */
function loadScript(src: string): Promise<void> {
  // Promise.withResolvers là ES2024, không có trong lib ES2023 của dự án này.
  return new Promise<void>((resolve, reject) => {
    const fail = () => reject(new Error(`Không thể tải VNPT eKYC SDK: ${src}`));

    const existing = findExistingScript(src);
    if (existing) {
      if (existing.getAttribute(LOADED_ATTR) === 'true') {
        resolve();
      } else {
        existing.addEventListener('load', () => resolve(), { once: true });
        existing.addEventListener('error', fail, { once: true });
      }
      return;
    }

    const script = document.createElement('script');
    script.src = src;
    script.async = false;
    script.onload = () => {
      script.setAttribute(LOADED_ATTR, 'true');
      resolve();
    };
    script.onerror = () => {
      script.remove();
      fail();
    };
    document.head.appendChild(script);
  });
}

async function injectAndWaitForSdk(): Promise<VnptEkycSdk> {
  for (const src of SDK_SCRIPT_SOURCES) {
    await loadScript(src);
  }

  const sdk = window.SDK;
  if (sdk === undefined) {
    throw new Error(
      'VNPT eKYC SDK đã tải xong nhưng window.SDK không khả dụng. Kiểm tra asset /ekyc-sdk/web-sdk-version-3.0.js.',
    );
  }

  return sdk;
}

/**
 * Nạp SDK và trả về entry `window.SDK`.
 * Idempotent: các lời gọi lặp lại dùng chung một promise; nếu SDK đã sẵn sàng thì resolve ngay.
 */
export function loadVnptEkycSdk(): Promise<VnptEkycSdk> {
  if (typeof window === 'undefined' || typeof document === 'undefined') {
    return Promise.reject(new Error('VNPT eKYC SDK chỉ có thể nạp trong môi trường trình duyệt.'));
  }

  const existingSdk = window.SDK;
  if (existingSdk !== undefined && window.FaceVNPTBrowserSDK !== undefined) {
    return Promise.resolve(existingSdk);
  }

  if (sdkLoadPromise === null) {
    sdkLoadPromise = injectAndWaitForSdk().catch((error: unknown) => {
      // Cho phép thử lại sau khi lỗi mạng/tải script.
      sdkLoadPromise = null;
      throw error;
    });
  }

  return sdkLoadPromise;
}

/**
 * Xóa trạng thái cache của loader để unit test độc lập.
 * Chỉ dùng trong test: gỡ các thẻ script đã chèn và reset promise dùng chung.
 * Các global `window.SDK` / `window.FaceVNPTBrowserSDK` do test tự stub và tự dọn.
 */
export function resetVnptSdkLoaderForTests(): void {
  sdkLoadPromise = null;

  if (typeof document === 'undefined') {
    return;
  }

  for (const src of SDK_SCRIPT_SOURCES) {
    for (const script of Array.from(
      document.querySelectorAll<HTMLScriptElement>(`script[src="${src}"]`),
    )) {
      script.remove();
    }
  }
}
