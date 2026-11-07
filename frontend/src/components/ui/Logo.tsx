import Image from "next/image";

interface LogoProps {
  className?: string;
  /** Cargar con prioridad cuando el logo está visible en la parte superior. */
  eager?: boolean;
}

export function Logo({ className = "h-11", eager = false }: LogoProps) {
  return (
    <Image
      src="/images/nettes-logo.png"
      alt="Mini Super Nette's"
      width={288}
      height={176}
      loading={eager ? "eager" : "lazy"}
      draggable={false}
      className={`${className} w-auto rounded-lg select-none`}
    />
  );
}

