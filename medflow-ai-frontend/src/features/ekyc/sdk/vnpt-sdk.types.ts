/**
 * Kiểu dữ liệu cho VNPT eKYC Web SDK (bundle 3.0).
 *
 * SDK được nhúng dưới dạng script toàn cục, không phải npm package:
 * - `/ekyc-sdk/web-sdk-version-3.0.js` gán `window.SDK` (entry duy nhất để gọi `launch`).
 * - `/ekyc-sdk/lib/VNPTQRBrowserApp.js` và `/ekyc-sdk/lib/VNPTBrowserSDKAppV4.1.0.js`
 *   bổ sung `window.FaceVNPTBrowserSDK`; bundle chính tự await và gọi `.init()`, phía
 *   frontend không gọi trực tiếp.
 *
 * Quy ước đặt tên khóa giữ nguyên như SDK (kể cả các lỗi chính tả) để config truyền
 * xuống không bị SDK bỏ qua.
 */

/**
 * Kết quả trả về từ `CALL_BACK_END_FLOW` / `CALL_BACK_DOCUMENT_RESULT`.
 *
 * Đây là bằng chứng eKYC do SDK/VNPT sinh ra; cấu trúc thật là độc quyền và có thể
 * thay đổi theo phiên bản. Frontend KHÔNG diễn giải object này — chỉ chuyển nguyên
 * trạng cho backend finalize. Vì vậy các khóa quen thuộc được khai báo `unknown`
 * kèm index signature để chấp nhận mọi khóa lạ.
 */
export interface VnptEkycResult {
  /** Kết quả OCR giấy tờ (thường chứa dữ liệu đã chuẩn hóa + hash). */
  ocr?: unknown;
  /** Bằng chứng liveness mặt trước giấy tờ. */
  liveness_card_front?: unknown;
  /** Bằng chứng liveness mặt sau giấy tờ. */
  liveness_card_back?: unknown;
  /** Bằng chứng liveness khuôn mặt. */
  liveness_face?: unknown;
  /** Kết quả so khớp khuôn mặt với ảnh giấy tờ. */
  compare?: unknown;
  /** Kết quả che/mask khuôn mặt. */
  masked?: unknown;
  /** Hash ảnh. */
  hash_img?: unknown;
  /** Hash dữ liệu giấy tờ. */
  data_hash_document?: unknown;
  /** Ảnh giấy tờ dạng base64. */
  base64_doc_img?: unknown;
  /** Ảnh khuôn mặt dạng base64. */
  base64_face_img?: unknown;
  /** Chữ ký dữ liệu do VNPT cấp. */
  dataSign?: unknown;
  /** Dữ liệu tổng hợp dạng base64. */
  dataBase64?: unknown;
  /** Dữ liệu QR đọc được (nếu có). */
  qrCode?: unknown;
  /** Loại giấy tờ được chọn. */
  type_document?: unknown;
  /** Mã phiên phía client do SDK sinh. */
  client_session?: unknown;
  /** Cho phép mọi khóa khác mà phiên bản SDK hiện tại trả về. */
  [key: string]: unknown;
}

/** Chế độ luồng chụp. Giá trị khớp với hằng số của SDK. */
export type VnptEkycSdkFlow = 'DOCUMENT_TO_FACE' | 'FACE_TO_DOCUMENT' | 'FACE' | 'DOCUMENT';

/** Phương thức nhập ảnh giấy tờ. */
export type VnptEkycUseMethod = 'BOTH' | 'PHOTO' | 'UPLOAD';

/** Ngôn ngữ giao diện SDK. */
export type VnptEkycLanguage = 'vi' | 'en';

/** Khóa luồng thực tế mà bundle 3.0 đọc (xem ghi chú ở `FLOW_TAKEN`). */
export type VnptEkycFlowTaken = 'BOTH' | 'DOCUMENT' | 'FACE';

/**
 * Cấu hình truyền vào `window.SDK.launch(config)`.
 *
 * Lưu ý các trường bắt buộc:
 * - `BACKEND_URL`: URL gốc của dịch vụ VNPT (proxy của backend MedFlowAI).
 * - `AUTHORIZION`: tên trường đúng như SDK đọc, bao gồm lỗi chính tả (không phải
 *   `AUTHORIZATION`). SDK KHÔNG đọc `ACCESS_TOKEN` khi gọi API, vì vậy đây là nơi
 *   duy nhất để truyền access token dạng chuỗi `Bearer ...` nếu cần.
 * - `TOKEN_KEY` / `TOKEN_ID`: khóa và mã định danh token do VNPT cấp.
 * - `CALL_BACK_END_FLOW`: được gọi khi toàn bộ luồng kết thúc, nhận bằng chứng eKYC.
 *
 * Frontend phải tự tạo div id `ekyc_sdk_intergrated` (đúng lỗi chính tả này) trước khi
 * gọi `launch`; SDK render widget vào chính div đó.
 */
export interface VnptEkycConfig {
  /** URL gốc dịch vụ VNPT, ví dụ `https://<proxy>/api/v1/ekyc/proxy/<sessionId>`. */
  BACKEND_URL: string;
  /** Khóa token do VNPT cấp. */
  TOKEN_KEY: string;
  /** Mã token do VNPT cấp. */
  TOKEN_ID: string;
  /**
   * Giá trị header `Authorization` của SDK — tên trường giữ nguyên lỗi chính tả
   * `AUTHORIZION` như bundle đọc. SDK không đọc `ACCESS_TOKEN`; hãy truyền token tại đây.
   */
  AUTHORIZION: string;
  /** Callback kết thúc luồng; nhận object bằng chứng không được diễn giải ở frontend. */
  CALL_BACK_END_FLOW: (result: VnptEkycResult) => void;

  /** Access token tùy chọn (SDK 3.0 không đọc; giữ để tương thích tài liệu tích hợp). */
  ACCESS_TOKEN?: string;
  /**
   * Luồng chụp theo tài liệu tích hợp VNPT.
   * Bundle 3.0 thực tế đọc `FLOW_TAKEN`; nếu cần điều khiển luồng, đặt cả hai khóa.
   */
  SDK_FLOW?: VnptEkycSdkFlow;
  /**
   * Luồng chụp thực tế bundle 3.0 so sánh trực tiếp. Giá trị hỗ trợ: `BOTH`, `DOCUMENT`, `FACE`.
   * Mặc định của SDK là `BOTH`.
   */
  FLOW_TAKEN?: VnptEkycFlowTaken;
  /** Danh sách loại giấy tờ cho phép, ví dụ `[-1, 4, 5, 6, 7, 9]`. */
  LIST_TYPE_DOCUMENT?: number[];
  /** Loại giấy tờ bắt đầu; `999` nghĩa là để SDK tự chọn. */
  DOCUMENT_TYPE_START?: number;
  /** Ngôn ngữ giao diện. */
  DEFAULT_LANGUAGE?: VnptEkycLanguage;
  /** Bật quét QR trên giấy tờ. */
  HAS_QR_SCAN?: boolean;
  /** Phương thức nhập ảnh giấy tờ. */
  USE_METHOD?: VnptEkycUseMethod;
  /** Bật kiểm tra liveness cho ảnh giấy tờ. */
  ENABLE_API_LIVENESS_DOCUMENT?: boolean;
  /** Bật kiểm tra liveness cho khuôn mặt. */
  ENABLE_API_LIVENESS_FACE?: boolean;
  /** Bật che/mask khuôn mặt. */
  ENABLE_API_MASKED_FACE?: boolean;
  /** Bật so khớp khuôn mặt với ảnh giấy tờ. */
  ENABLE_API_COMPARE_FACE?: boolean;
  /** Có hiển thị màn hình kết quả hay không. */
  HAS_RESULT_SCREEN?: boolean;
  /** Có hiển thị thanh tiến trình từng bước hay không. */
  SHOW_STEP?: boolean;
  /** Callback sau khi chụp xong giấy tờ (trước khi kết thúc toàn luồng). */
  CALL_BACK_DOCUMENT_RESULT?: (result: VnptEkycResult) => void;
  /** Đường dẫn file animation oval cho màn hình rộng; mặc định của SDK là `/lib/web-oval.json`. */
  URL_WEB_OVAL?: string;
  /** Đường dẫn file animation oval cho màn hình hẹp; mặc định là `/lib/mobile-oval.json`. */
  URL_MOBILE_OVAL?: string;
  /** Mã challenge dùng cho các API VNPT; mặc định `00000`. */
  CHALLENGE_CODE?: string;

  /**
   * Các khóa gốc bundle 3.0 đọc trực tiếp (khác tên với nhóm `ENABLE_API_*` ở trên).
   * Chỉ đặt khi cần ghi đè hành vi mặc định; mặc định của SDK đều là `true`.
   */
  CHECK_LIVENESS_CARD?: boolean;
  CHECK_LIVENESS_FACE?: boolean;
  CHECK_MASKED_FACE?: boolean;
  COMPARE_FACE?: boolean;
  /** Bật liveness hai lần (mặc định `false`). */
  DOUBLE_LIVENESS?: boolean;
  /** Nhãn camera giả dùng cho môi trường kiểm thử. */
  FAKE_CAM_LABEL?: string;
}

/** Entry toàn cục `window.SDK` của bundle chính. */
export interface VnptEkycSdk {
  /** Render widget eKYC vào div id `ekyc_sdk_intergrated`. */
  launch(config: VnptEkycConfig): void;
}

declare global {
  interface Window {
    /** Được gán bởi `/ekyc-sdk/web-sdk-version-3.0.js`. */
    SDK?: VnptEkycSdk;
    /**
     * Được gán bởi `/ekyc-sdk/lib/VNPTBrowserSDKAppV4.1.0.js`.
     * Bundle chính tự await rồi gọi `.init()`; frontend không thao tác trực tiếp.
     */
    FaceVNPTBrowserSDK?: unknown;
  }
}
