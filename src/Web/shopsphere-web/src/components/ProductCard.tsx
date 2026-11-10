"use client";

import { ShoppingCart } from "lucide-react";
import { useCart } from "@/context/CartContext";
import { productImage, type Product, type StockLevel } from "@/lib/catalog";
import { formatPrice } from "@/lib/format";
import { cn } from "@/lib/utils";
import { ProductImage } from "./ProductImage";

interface ProductCardProps {
  product: Product;
  /** `undefined` while loading, `null` when the product has no stock record. */
  stock: StockLevel | null | undefined;
}

function StockLine({ stock, available }: { stock: StockLevel | null | undefined; available: boolean }) {
  if (!available) return <span className="text-slate-500">Currently unavailable</span>;
  if (stock === undefined) return <span className="inline-block h-4 w-24 animate-pulse rounded bg-slate-100" />;
  if (stock === null) return <span className="text-slate-500">Coming soon</span>;
  if (stock.quantityAvailable <= 0) return <span className="font-medium text-rose-600">Out of stock</span>;
  const low = stock.quantityAvailable <= stock.restockThreshold;
  return (
    <span className={cn("font-medium", low ? "text-amber-600" : "text-emerald-600")}>
      {low ? `Only ${stock.quantityAvailable} left` : `In stock: ${stock.quantityAvailable} units`}
    </span>
  );
}

export function ProductCard({ product, stock }: ProductCardProps) {
  const { items, addItem } = useCart();
  const inCart = items.find((i) => i.productId === product.id)?.quantity ?? 0;
  const max = stock ? stock.quantityAvailable : undefined;
  // No stock record means the warehouse can't fulfil it yet, so it isn't purchasable.
  const soldOut = !product.isAvailable || stock === null || (max !== undefined && max <= 0);
  const atMax = max !== undefined && inCart >= max && !soldOut;
  const image = productImage(product);

  return (
    <article className="group flex flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm transition hover:-translate-y-0.5 hover:shadow-md">
      <ProductImage
        src={image}
        alt={product.name}
        category={product.category}
        className="aspect-[4/3] w-full"
        sizes="(min-width: 1280px) 25vw, (min-width: 640px) 50vw, 100vw"
      />
      <div className="flex flex-1 flex-col p-4">
        <p className="text-xs font-medium uppercase tracking-wide text-indigo-600">{product.category}</p>
        <h3 className="mt-1 line-clamp-2 font-semibold text-slate-900">{product.name}</h3>
        <p className="mt-1 line-clamp-2 text-sm text-slate-500">{product.description}</p>
        <div className="mt-auto pt-4">
          <div className="flex items-end justify-between gap-2">
            <p className="text-xl font-bold text-slate-900">{formatPrice(product.price)}</p>
            <p className="text-xs">
              <StockLine stock={stock} available={product.isAvailable} />
            </p>
          </div>
          <button
            type="button"
            disabled={soldOut || atMax}
            onClick={() =>
              addItem({ id: product.id, name: product.name, price: product.price, imageUrl: image, category: product.category }, max)
            }
            className="mt-3 flex w-full items-center justify-center gap-2 rounded-lg bg-indigo-600 py-2.5 text-sm font-semibold text-white transition hover:bg-indigo-500 disabled:cursor-not-allowed disabled:bg-slate-200 disabled:text-slate-500"
          >
            <ShoppingCart className="h-4 w-4" aria-hidden />
            {stock === null && product.isAvailable ? "Not yet available" : soldOut ? "Out of stock" : atMax ? "All available units in cart" : inCart ? `Add another (${inCart} in cart)` : "Add to cart"}
          </button>
        </div>
      </div>
    </article>
  );
}

export function ProductCardSkeleton() {
  return (
    <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white">
      <div className="aspect-[4/3] animate-pulse bg-slate-100" />
      <div className="space-y-3 p-4">
        <div className="h-3 w-20 animate-pulse rounded bg-slate-100" />
        <div className="h-4 w-3/4 animate-pulse rounded bg-slate-100" />
        <div className="h-3 w-full animate-pulse rounded bg-slate-100" />
        <div className="h-9 w-full animate-pulse rounded-lg bg-slate-100" />
      </div>
    </div>
  );
}
