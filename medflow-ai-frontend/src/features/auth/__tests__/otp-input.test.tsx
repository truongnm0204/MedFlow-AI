import { render, screen, fireEvent } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

import { OtpInput } from '../components/otp-input';

describe('OtpInput', () => {
  it('render 6 ô input', () => {
    render(<OtpInput value="" onChange={() => {}} />);
    expect(screen.getAllByRole('textbox')).toHaveLength(6);
  });

  it('gõ số → điền ô + auto-advance', () => {
    const onChange = vi.fn();
    render(<OtpInput value="" onChange={onChange} />);
    const inputs = screen.getAllByRole('textbox');

    fireEvent.change(inputs[0], { target: { value: '1' } });
    expect(onChange).toHaveBeenCalledWith('1');
  });

  it('backspace xoá ô hiện tại', () => {
    const onChange = vi.fn();
    render(<OtpInput value="12" onChange={onChange} />);
    const inputs = screen.getAllByRole('textbox');

    fireEvent.keyDown(inputs[1], { key: 'Backspace' });
    expect(onChange).toHaveBeenCalledWith('1');
  });

  it('paste fill-all các ô', () => {
    const onChange = vi.fn();
    render(<OtpInput value="" onChange={onChange} />);

    fireEvent.paste(screen.getAllByRole('textbox')[0], {
      clipboardData: { getData: () => '123456' },
    });
    expect(onChange).toHaveBeenCalledWith('123456');
  });

  it('paste lọc ký tự không phải số', () => {
    const onChange = vi.fn();
    render(<OtpInput value="" onChange={onChange} />);

    fireEvent.paste(screen.getAllByRole('textbox')[0], {
      clipboardData: { getData: () => '1a2b3c' },
    });
    expect(onChange).toHaveBeenCalledWith('123');
  });

  it('disabled → input disabled', () => {
    render(<OtpInput value="" onChange={() => {}} disabled />);
    for (const input of screen.getAllByRole('textbox')) {
      expect(input).toBeDisabled();
    }
  });
});
