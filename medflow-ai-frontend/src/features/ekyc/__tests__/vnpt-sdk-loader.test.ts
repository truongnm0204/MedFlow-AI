import { afterEach, beforeEach, describe, expect, it } from 'vitest';

import {
  loadVnptEkycSdk,
  resetVnptSdkLoaderForTests,
} from '../sdk/vnpt-sdk-loader';
import type { VnptEkycSdk } from '../sdk/vnpt-sdk.types';

const SDK_BASE_PATH = '/ekyc-sdk';
const EXPECTED_SOURCES = [
  `${SDK_BASE_PATH}/web-sdk-version-3.0.js`,
  `${SDK_BASE_PATH}/lib/VNPTQRBrowserApp.js`,
  `${SDK_BASE_PATH}/lib/VNPTBrowserSDKAppV4.1.0.js`,
];
const LOADED_ATTR = 'data-vnpt-ekyc-loaded';

function createFakeSdk(): VnptEkycSdk {
  return { launch: () => undefined };
}

/** Tất cả thẻ script SDK đang có trong document (theo thứ tự chèn). */
function sdkScripts(): HTMLScriptElement[] {
  return Array.from(document.querySelectorAll<HTMLScriptElement>('script[src]')).filter((script) =>
    (script.getAttribute('src') ?? '').startsWith(SDK_BASE_PATH),
  );
}

function sdkScriptSources(): string[] {
  return sdkScripts().map((script) => script.getAttribute('src') ?? '');
}

/** Nhả toàn bộ microtask đang chờ để chuỗi nạp tuần tự tiến tới thẻ kế tiếp. */
async function flushMicrotasks(): Promise<void> {
  for (let i = 0; i < 10; i += 1) {
    await Promise.resolve();
  }
}

function pendingSdkScripts(): HTMLScriptElement[] {
  return sdkScripts().filter((script) => script.getAttribute(LOADED_ATTR) !== 'true');
}

/**
 * Mô phỏng vòng đời nạp script: jsdom không thực thi `src`, nên ta phát sự kiện `load`
 * cho thẻ mới nhất đang chờ, đúng như trình duyệt thật sẽ làm.
 */
async function dispatchNextScriptLoad(): Promise<void> {
  await flushMicrotasks();
  const pending = pendingSdkScripts();
  const script = pending[pending.length - 1];
  if (!script) {
    throw new Error('Không có thẻ script VNPT eKYC nào đang chờ nạp.');
  }
  script.dispatchEvent(new Event('load'));
}

/** Hoàn tất việc nạp cả ba thẻ script theo đúng thứ tự phụ thuộc. */
async function loadAllSdkScripts(): Promise<void> {
  for (let i = 0; i < EXPECTED_SOURCES.length; i += 1) {
    await dispatchNextScriptLoad();
  }
  await flushMicrotasks();
}

describe('vnpt-sdk-loader', () => {
  beforeEach(() => {
    resetVnptSdkLoaderForTests();
    delete window.SDK;
    delete window.FaceVNPTBrowserSDK;
  });

  afterEach(() => {
    resetVnptSdkLoaderForTests();
    delete window.SDK;
    delete window.FaceVNPTBrowserSDK;
  });

  it('trả về ngay SDK toàn cục khi cả hai global đã sẵn sàng, không chèn script', async () => {
    const sdk = createFakeSdk();
    window.SDK = sdk;
    window.FaceVNPTBrowserSDK = {};

    await expect(loadVnptEkycSdk()).resolves.toBe(sdk);
    expect(sdkScripts()).toHaveLength(0);
  });

  it('single-flight: hai lời gọi đồng thời dùng chung một promise và một bộ script', async () => {
    const sdk = createFakeSdk();
    window.SDK = sdk;

    const first = loadVnptEkycSdk();
    const second = loadVnptEkycSdk();

    expect(first).toBe(second);
    // Chỉ thẻ đầu tiên được chèn cho tới khi nó nạp xong.
    await flushMicrotasks();
    expect(sdkScripts()).toHaveLength(1);

    await loadAllSdkScripts();

    await expect(first).resolves.toBe(sdk);
    await expect(second).resolves.toBe(sdk);
    expect(sdkScriptSources()).toEqual(EXPECTED_SOURCES);
  });

  it('nạp tuần tự đúng ba script theo thứ tự phụ thuộc bắt buộc', async () => {
    window.SDK = createFakeSdk();

    const promise = loadVnptEkycSdk();

    for (let index = 0; index < EXPECTED_SOURCES.length; index += 1) {
      await flushMicrotasks();

      // Trước khi thẻ hiện tại nạp xong: chỉ có nó đang chờ, chưa chèn thẻ kế tiếp.
      expect(sdkScripts()).toHaveLength(index + 1);
      expect(pendingSdkScripts().map((script) => script.getAttribute('src'))).toEqual([
        EXPECTED_SOURCES[index],
      ]);

      await dispatchNextScriptLoad();
      expect(sdkScripts()[index].getAttribute(LOADED_ATTR)).toBe('true');
    }

    await flushMicrotasks();
    await expect(promise).resolves.toBe(window.SDK);
    expect(sdkScriptSources()).toEqual(EXPECTED_SOURCES);
    // Mỗi thẻ được đánh dấu đã nạp để lần sau không chèn trùng.
    for (const script of sdkScripts()) {
      expect(script.getAttribute(LOADED_ATTR)).toBe('true');
    }
  });

  it('không chèn trùng thẻ script khi gọi lặp lại sau khi đã nạp', async () => {
    window.SDK = createFakeSdk();

    const first = loadVnptEkycSdk();
    await loadAllSdkScripts();
    await expect(first).resolves.toBe(window.SDK);
    expect(sdkScripts()).toHaveLength(EXPECTED_SOURCES.length);

    const second = loadVnptEkycSdk();
    await flushMicrotasks();

    await expect(second).resolves.toBe(window.SDK);
    expect(sdkScripts()).toHaveLength(EXPECTED_SOURCES.length);
    expect(sdkScriptSources()).toEqual(EXPECTED_SOURCES);
  });

  it('tái sử dụng thẻ script đã nạp sẵn (HMR) thay vì chèn thêm', async () => {
    const sdk = createFakeSdk();
    window.SDK = sdk;
    for (const src of EXPECTED_SOURCES) {
      const script = document.createElement('script');
      script.setAttribute('src', src);
      script.setAttribute(LOADED_ATTR, 'true');
      document.head.appendChild(script);
    }

    await expect(loadVnptEkycSdk()).resolves.toBe(sdk);
    expect(sdkScripts()).toHaveLength(EXPECTED_SOURCES.length);
    expect(sdkScriptSources()).toEqual(EXPECTED_SOURCES);
  });

  it('chờ thẻ script đang nạp dở thay vì chèn thẻ trùng', async () => {
    const sdk = createFakeSdk();
    window.SDK = sdk;

    const inFlight = document.createElement('script');
    inFlight.setAttribute('src', EXPECTED_SOURCES[0]);
    document.head.appendChild(inFlight);

    const promise = loadVnptEkycSdk();
    await flushMicrotasks();

    // Không có thẻ thứ hai cho cùng một src.
    expect(sdkScripts().filter((script) => script.getAttribute('src') === EXPECTED_SOURCES[0])).toHaveLength(1);

    inFlight.dispatchEvent(new Event('load'));

    await loadAllSdkScripts();
    await expect(promise).resolves.toBe(sdk);
    expect(sdkScriptSources()).toEqual(EXPECTED_SOURCES);
  });

  it('từ chối khi script lỗi, gỡ thẻ lỗi và cho phép thử lại', async () => {
    const sdk = createFakeSdk();
    window.SDK = sdk;

    const promise = loadVnptEkycSdk();
    const rejection = expect(promise).rejects.toThrow(
      `Không thể tải VNPT eKYC SDK: ${EXPECTED_SOURCES[0]}`,
    );

    await flushMicrotasks();
    const firstScript = sdkScripts()[0];
    firstScript.dispatchEvent(new Event('error'));

    await rejection;
    expect(sdkScripts()).toHaveLength(0);

    // Cache promise đã được reset: lần gọi sau nạp lại từ đầu.
    const retry = loadVnptEkycSdk();
    const retryRejection = expect(retry).resolves.toBe(sdk);
    await loadAllSdkScripts();
    await retryRejection;
    expect(sdkScriptSources()).toEqual(EXPECTED_SOURCES);
  });

  it('từ chối khi tải xong script nhưng window.SDK không khả dụng', async () => {
    const promise = loadVnptEkycSdk();
    const rejection = expect(promise).rejects.toThrow(
      'VNPT eKYC SDK đã tải xong nhưng window.SDK không khả dụng',
    );

    await loadAllSdkScripts();

    await rejection;
    expect(sdkScripts()).toHaveLength(EXPECTED_SOURCES.length);
  });

  it('resetVnptSdkLoaderForTests gỡ toàn bộ script và xoá promise dùng chung', async () => {
    window.SDK = createFakeSdk();

    const first = loadVnptEkycSdk();
    await loadAllSdkScripts();
    await first;
    expect(sdkScripts()).toHaveLength(EXPECTED_SOURCES.length);

    resetVnptSdkLoaderForTests();

    expect(sdkScripts()).toHaveLength(0);

    // Sau reset, một lời gọi mới phải nạp lại từ đầu thay vì dùng promise cũ.
    const second = loadVnptEkycSdk();
    await flushMicrotasks();
    expect(sdkScripts()).toHaveLength(1);

    const resolved = expect(second).resolves.toBe(window.SDK);
    await loadAllSdkScripts();
    await resolved;
    expect(sdkScriptSources()).toEqual(EXPECTED_SOURCES);
  });
});
