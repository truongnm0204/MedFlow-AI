import { describe, expect, it, vi } from 'vitest';

import {
  EKYC_SDK_CONTAINER_ID,
  VNPT_PLACEHOLDER_CREDENTIAL,
  buildEkycSdkBackendUrl,
  buildEkycSdkConfig,
} from '../sdk/ekyc-sdk-config';

const PROXY_PATH = '/api/v1/ekyc/proxy/123';
const ORIGIN = 'https://app.medflow.vn';
const EXPECTED_BACKEND_URL = 'https://app.medflow.vn/api/v1/ekyc/proxy/123';

describe('buildEkycSdkBackendUrl', () => {
  it('ghép origin và proxyBasePath với đúng một dấu slash', () => {
    const variants: Array<{ origin: string; path: string }> = [
      { origin: 'https://app.medflow.vn', path: PROXY_PATH },
      { origin: 'https://app.medflow.vn/', path: PROXY_PATH },
      { origin: 'https://app.medflow.vn', path: 'api/v1/ekyc/proxy/123' },
      { origin: 'https://app.medflow.vn/', path: 'api/v1/ekyc/proxy/123' },
      { origin: 'https://app.medflow.vn//', path: '//api/v1/ekyc/proxy/123' },
    ];

    for (const { origin, path } of variants) {
      const url = buildEkycSdkBackendUrl(path, origin);
      expect(url, `${origin} + ${path}`).toBe(EXPECTED_BACKEND_URL);
      // Không bao giờ sinh ra "//" trong phần đường dẫn.
      expect(url.slice('https://'.length)).not.toContain('//');
    }
  });

  it('mặc định dùng origin hiện tại của trình duyệt', () => {
    expect(buildEkycSdkBackendUrl(PROXY_PATH)).toBe(`${window.location.origin}${PROXY_PATH}`);
  });

  it('trả về origin đã chuẩn hóa khi proxyBasePath rỗng', () => {
    expect(buildEkycSdkBackendUrl('', 'https://app.medflow.vn/')).toBe('https://app.medflow.vn');
    expect(buildEkycSdkBackendUrl('/', 'https://app.medflow.vn//')).toBe('https://app.medflow.vn');
  });
});

describe('buildEkycSdkConfig', () => {
  const onEndFlow = vi.fn();
  const onDocumentResult = vi.fn();

  it('trỏ BACKEND_URL vào proxy của backend, kết thúc bằng proxyBasePath', () => {
    const config = buildEkycSdkConfig({
      proxyBasePath: PROXY_PATH,
      origin: ORIGIN,
      onEndFlow,
    });

    expect(config.BACKEND_URL).toBe(EXPECTED_BACKEND_URL);
    expect(config.BACKEND_URL.endsWith(PROXY_PATH)).toBe(true);
  });

  it('chỉ dùng placeholder không bí mật cho cả bốn trường thông tin xác thực', () => {
    const config = buildEkycSdkConfig({
      proxyBasePath: PROXY_PATH,
      origin: ORIGIN,
      onEndFlow,
    });

    expect(VNPT_PLACEHOLDER_CREDENTIAL.trim()).not.toBe('');
    expect(config.TOKEN_KEY).toBe(VNPT_PLACEHOLDER_CREDENTIAL);
    expect(config.TOKEN_ID).toBe(VNPT_PLACEHOLDER_CREDENTIAL);
    // Tên trường giữ nguyên lỗi chính tả của vendor SDK.
    expect(config.AUTHORIZION).toBe(VNPT_PLACEHOLDER_CREDENTIAL);
    expect(config.ACCESS_TOKEN).toBe(VNPT_PLACEHOLDER_CREDENTIAL);
  });

  it('chỉ chụp giấy tờ (DOCUMENT), tắt QR và cấu hình CCDD (9)', () => {
    const config = buildEkycSdkConfig({
      proxyBasePath: PROXY_PATH,
      origin: ORIGIN,
      onEndFlow,
    });

    expect(config.FLOW_TAKEN).toBe('DOCUMENT');
    expect(config.HAS_QR_SCAN).toBe(false);
    expect(config.USE_METHOD).toBe('BOTH');
    expect(config.LIST_TYPE_DOCUMENT).toEqual([9]);
    expect(config.DOCUMENT_TYPE_START).toBe(9);
  });

  it('phạm vi chỉ CCCD: bật liveness giấy tờ, tắt khuôn mặt và so khớp khuôn mặt', () => {
    const config = buildEkycSdkConfig({
      proxyBasePath: PROXY_PATH,
      origin: ORIGIN,
      onEndFlow,
    });

    expect(config.CHECK_LIVENESS_CARD).toBe(true);
    expect(config.CHECK_LIVENESS_FACE).toBe(false);
    expect(config.CHECK_MASKED_FACE).toBe(false);
    expect(config.COMPARE_FACE).toBe(false);
  });

  it('gắn nguyên callback kết thúc luồng do người gọi truyền vào', () => {
    const config = buildEkycSdkConfig({
      proxyBasePath: PROXY_PATH,
      origin: ORIGIN,
      onEndFlow,
    });

    expect(config.CALL_BACK_END_FLOW).toBe(onEndFlow);
  });

  it('chỉ gắn callback kết quả giấy tờ khi được truyền vào', () => {
    const withoutDocumentCallback = buildEkycSdkConfig({
      proxyBasePath: PROXY_PATH,
      origin: ORIGIN,
      onEndFlow,
    });
    expect(withoutDocumentCallback.CALL_BACK_DOCUMENT_RESULT).toBeUndefined();

    const withDocumentCallback = buildEkycSdkConfig({
      proxyBasePath: PROXY_PATH,
      origin: ORIGIN,
      onEndFlow,
      onDocumentResult,
    });
    expect(withDocumentCallback.CALL_BACK_DOCUMENT_RESULT).toBe(onDocumentResult);
  });

  it('không chia sẻ mảng cấu hình giữa hai lần build (không rò rỉ mutation)', () => {
    const options = { proxyBasePath: PROXY_PATH, origin: ORIGIN, onEndFlow };
    const first = buildEkycSdkConfig(options);
    const second = buildEkycSdkConfig(options);

    expect(first.LIST_TYPE_DOCUMENT).not.toBe(second.LIST_TYPE_DOCUMENT);
    expect(first.LIST_TYPE_DOCUMENT).toEqual(second.LIST_TYPE_DOCUMENT);
  });
});

describe('EKYC_SDK_CONTAINER_ID', () => {
  it('giữ nguyên id sai chính tả mà vendor SDK hardcode', () => {
    expect(EKYC_SDK_CONTAINER_ID).toBe('ekyc_sdk_intergrated');
  });
});
