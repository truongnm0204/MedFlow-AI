import { Link } from 'react-router-dom';

type Variant = 'icon' | 'full';
type Tone = 'color' | 'white';

const SOURCES: Record<`${Variant}-${Tone}`, string> = {
  'icon-color': '/logo-icon.png',
  'icon-white': '/logo-icon-white.png',
  'full-color': '/logo-full.png',
  'full-white': '/logo-full-white.png',
};

interface MedflowLogoProps {
  variant?: Variant;
  tone?: Tone;
  className?: string;
  /** Wrap in a Link to /. Default true. */
  linkToHome?: boolean;
}

export function MedflowLogo({
  variant = 'icon',
  tone = 'color',
  className = 'h-10 w-auto',
  linkToHome = true,
}: MedflowLogoProps) {
  const img = (
    <img
      src={SOURCES[`${variant}-${tone}`]}
      alt="MedFlow AI"
      className={className}
      loading="eager"
      decoding="async"
    />
  );
  return linkToHome ? (
    <Link to="/" aria-label="MedFlow AI - Trang chủ" className="inline-flex items-center">
      {img}
    </Link>
  ) : (
    img
  );
}
