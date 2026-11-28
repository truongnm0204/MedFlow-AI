import { fireEvent, render, screen } from '@testing-library/react';
import { useState } from 'react';
import { describe, expect, it, vi } from 'vitest';

import { EkycConsentScreen } from '../components/ekyc-consent-screen';

const CONSENT_VERSION = 'v2025.1';
const CONSENT_TEXT = 'Tôi đồng ý cho MedFlowAI xử lý dữ liệu sinh trắc học để xác minh danh tính.';
const CHECKBOX_LABEL = 'Tôi đã đọc và đồng ý với các điều khoản trên';
const CONFIRM_LABEL = 'Đồng ý và tiếp tục';
const SUBMITTING_LABEL = 'Đang xử lý…';

interface HarnessProps {
  onConfirm: () => void;
  onCancel?: () => void;
  submitting?: boolean;
}

/** Mô phỏng component cha quản lý trạng thái `accepted` (component thuần hiển thị). */
function ConsentHarness({ onConfirm, onCancel, submitting = false }: HarnessProps) {
  const [accepted, setAccepted] = useState(false);

  return (
    <EkycConsentScreen
      consentVersion={CONSENT_VERSION}
      consentText={CONSENT_TEXT}
      accepted={accepted}
      onAcceptedChange={setAccepted}
      onConfirm={onConfirm}
      onCancel={onCancel}
      submitting={submitting}
    />
  );
}

function getConfirmButton(): HTMLElement {
  return screen.getByRole('button', { name: CONFIRM_LABEL });
}

describe('EkycConsentScreen', () => {
  it('hiển thị tiêu đề, phiên bản và toàn bộ nội dung điều khoản đồng ý', () => {
    render(<ConsentHarness onConfirm={vi.fn()} />);

    expect(
      screen.getByRole('heading', { name: 'Đồng ý xác thực danh tính điện tử (eKYC)' }),
    ).toBeInTheDocument();
    expect(screen.getByText('Phiên bản điều khoản')).toBeInTheDocument();
    expect(screen.getByText(CONSENT_VERSION)).toBeInTheDocument();

    const documentRegion = screen.getByRole('document', {
      name: 'Nội dung điều khoản đồng ý eKYC',
    });
    expect(documentRegion).toHaveTextContent(CONSENT_TEXT);
  });

  it('hiển thị checkbox đồng ý chưa tích và nút xác nhận bị vô hiệu hoá', () => {
    render(<ConsentHarness onConfirm={vi.fn()} />);

    const checkbox = screen.getByRole('checkbox', { name: CHECKBOX_LABEL });
    expect(checkbox).not.toBeChecked();

    const confirmButton = getConfirmButton();
    expect(confirmButton).toBeDisabled();
  });

  it('tích đồng ý thì bật nút xác nhận, bỏ tích thì vô hiệu hoá lại', () => {
    render(<ConsentHarness onConfirm={vi.fn()} />);

    const checkbox = screen.getByRole('checkbox', { name: CHECKBOX_LABEL });
    fireEvent.click(checkbox);
    expect(checkbox).toBeChecked();
    expect(getConfirmButton()).toBeEnabled();

    fireEvent.click(checkbox);
    expect(checkbox).not.toBeChecked();
    expect(getConfirmButton()).toBeDisabled();
  });

  it('gọi onConfirm đúng một lần khi bấm nút sau khi đã đồng ý', () => {
    const onConfirm = vi.fn();
    render(<ConsentHarness onConfirm={onConfirm} />);

    fireEvent.click(screen.getByRole('checkbox', { name: CHECKBOX_LABEL }));
    fireEvent.click(getConfirmButton());

    expect(onConfirm).toHaveBeenCalledTimes(1);
  });

  it('không gọi onConfirm khi nút xác nhận còn bị vô hiệu hoá', () => {
    const onConfirm = vi.fn();
    render(<ConsentHarness onConfirm={onConfirm} />);

    fireEvent.click(getConfirmButton());

    expect(onConfirm).not.toHaveBeenCalled();
  });

  it('không hiển thị nút Huỷ khi không truyền onCancel, hiển thị và gọi được khi có', () => {
    const onCancel = vi.fn();
    const { unmount } = render(<ConsentHarness onConfirm={vi.fn()} />);
    expect(screen.queryByRole('button', { name: 'Hủy' })).not.toBeInTheDocument();
    unmount();

    render(<ConsentHarness onConfirm={vi.fn()} onCancel={onCancel} />);
    const cancelButton = screen.getByRole('button', { name: 'Hủy' });
    fireEvent.click(cancelButton);
    expect(onCancel).toHaveBeenCalledTimes(1);
  });

  it('khi đang gửi thì vô hiệu hoá checkbox và nút xác nhận, hiện trạng thái xử lý', () => {
    const onConfirm = vi.fn();
    render(
      <EkycConsentScreen
        consentVersion={CONSENT_VERSION}
        consentText={CONSENT_TEXT}
        accepted
        onAcceptedChange={vi.fn()}
        onConfirm={onConfirm}
        submitting
      />,
    );

    const checkbox = screen.getByRole('checkbox', { name: CHECKBOX_LABEL });
    expect(checkbox).toBeDisabled();

    const busyButton = screen.getByRole('button', { name: SUBMITTING_LABEL });
    expect(busyButton).toBeDisabled();
    expect(busyButton).toHaveTextContent(SUBMITTING_LABEL);
    expect(busyButton).toHaveAttribute('aria-busy', 'true');
    expect(screen.queryByText(CONFIRM_LABEL)).not.toBeInTheDocument();

    fireEvent.click(busyButton);
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it('khi đang gửi thì nút Huỷ cũng bị vô hiệu hoá', () => {
    render(
      <EkycConsentScreen
        consentVersion={CONSENT_VERSION}
        consentText={CONSENT_TEXT}
        accepted
        onAcceptedChange={vi.fn()}
        onConfirm={vi.fn()}
        onCancel={vi.fn()}
        submitting
      />,
    );

    expect(screen.getByRole('button', { name: 'Hủy' })).toBeDisabled();
  });
});
