type Size = 'sm' | 'md' | 'lg';

const SIZES: Record<Size, string> = {
  sm: 'h-8 w-8 text-xs',
  md: 'h-10 w-10 text-sm',
  lg: 'h-12 w-12 text-base',
};

interface AvatarProps {
  name?: string | null;
  size?: Size;
  className?: string;
}

function initials(name: string): string {
  return name
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map((w) => w[0]?.toUpperCase() ?? '')
    .join('');
}

/** Avatar chữ cái đầu theo Figma `10:106` (nền primary, bo góc 10px). */
export function Avatar({ name, size = 'sm', className = '' }: AvatarProps) {
  return (
    <span
      className={`inline-flex items-center justify-center rounded-[10px] bg-primary font-semibold text-white ${SIZES[size]} ${className}`}
      aria-hidden
    >
      {initials(name ?? '') || '?'}
    </span>
  );
}
