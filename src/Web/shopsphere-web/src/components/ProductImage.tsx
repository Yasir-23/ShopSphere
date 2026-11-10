"use client";

import Image from "next/image";
import { useState } from "react";
import { Headphones, Keyboard, Lightbulb, Package } from "lucide-react";
import { cn } from "@/lib/utils";

const CATEGORY_ICON: Record<string, typeof Package> = {
  electronics: Headphones,
  peripherals: Keyboard,
  "smart home": Lightbulb,
};

interface ProductImageProps {
  src?: string | null;
  alt: string;
  category?: string;
  className?: string;
  iconClassName?: string;
  sizes?: string;
}

/** Product photo with a branded placeholder for missing or broken image URLs. */
export function ProductImage({ src, alt, category, className, iconClassName, sizes = "300px" }: ProductImageProps) {
  const [failedSrc, setFailedSrc] = useState<string | null>(null);
  const showImage = !!src && failedSrc !== src;
  const Icon = CATEGORY_ICON[category?.toLowerCase() ?? ""] ?? Package;

  return (
    <div className={cn("relative overflow-hidden bg-gradient-to-br from-slate-100 to-indigo-50", className)}>
      {showImage ? (
        <Image
          src={src}
          alt={alt}
          fill
          unoptimized
          sizes={sizes}
          className="object-cover"
          onError={() => setFailedSrc(src)}
        />
      ) : (
        <div className="flex h-full w-full items-center justify-center" role="img" aria-label={alt}>
          <Icon className={cn("h-1/3 w-1/3 max-h-16 max-w-16 text-indigo-300", iconClassName)} strokeWidth={1.25} />
        </div>
      )}
    </div>
  );
}
