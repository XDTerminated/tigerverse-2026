interface IconProps {
  name:
    | 'backward'
    | 'forward'
    | 'delete'
    | 'pencil'
    | 'tick'
    | 'magic-wand'
    | 'download'
    | 'cross'
    | 'sync';
  className?: string;
  spin?: boolean;
}

export function Icon({ name, className = 'w-5 h-5', spin = false }: IconProps) {
  return (
    <span
      aria-hidden="true"
      className={`inline-block bg-current shrink-0 ${spin ? 'animate-spin' : ''} ${className}`}
      style={{
        WebkitMaskImage: `url(/icons/${name}.svg)`,
        maskImage: `url(/icons/${name}.svg)`,
        WebkitMaskRepeat: 'no-repeat',
        maskRepeat: 'no-repeat',
        WebkitMaskPosition: 'center',
        maskPosition: 'center',
        WebkitMaskSize: 'contain',
        maskSize: 'contain',
      }}
    />
  );
}
