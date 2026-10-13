interface DividerProps {
  label?: string;
  className?: string;
}

/** Đường kẻ ngang, optional label ở giữa (vd "hoặc" — Figma `1:2`). */
export function Divider({ label, className = '' }: DividerProps) {
  if (!label) {
    return <div className={`border-t border-border ${className}`} role="separator" />;
  }
  return (
    <div className={`relative flex items-center ${className}`} role="separator">
      <div className="grow border-t border-border" />
      <span className="bg-white px-3 text-xs text-slate-400">{label}</span>
      <div className="grow border-t border-border" />
    </div>
  );
}
