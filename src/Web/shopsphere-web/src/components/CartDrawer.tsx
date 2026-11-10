"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useState } from "react";
import { Minus, Plus, ShoppingCart, Trash2, X } from "lucide-react";
import { useAuth } from "@/context/AuthContext";
import { useCart } from "@/context/CartContext";
import { formatPrice } from "@/lib/format";
import { cn } from "@/lib/utils";
import { ProductImage } from "./ProductImage";
import { CheckoutModal } from "./CheckoutModal";

export function CartDrawer() {
  const { items, totalItems, subtotal, isOpen, closeCart, setQuantity, removeItem, syncError } = useCart();
  const { user } = useAuth();
  const pathname = usePathname();
  const [checkoutOpen, setCheckoutOpen] = useState(false);

  useEffect(() => {
    if (!isOpen) return;
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && closeCart();
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    window.addEventListener("keydown", onKey);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener("keydown", onKey);
    };
  }, [isOpen, closeCart]);

  return (
    <>
      <div
        className={cn(
          "fixed inset-0 z-50 bg-slate-900/40 transition-opacity",
          isOpen ? "opacity-100" : "pointer-events-none opacity-0"
        )}
        onClick={closeCart}
        aria-hidden
      />
      <aside
        role="dialog"
        aria-modal="true"
        aria-label="Shopping cart"
        aria-hidden={!isOpen}
        inert={!isOpen}
        className={cn(
          "fixed inset-y-0 right-0 z-50 flex w-full max-w-md flex-col bg-white shadow-2xl transition-transform duration-300",
          isOpen ? "translate-x-0" : "translate-x-full"
        )}
      >
        <div className="flex items-center justify-between border-b border-slate-200 px-5 py-4">
          <h2 className="text-lg font-semibold text-slate-900">
            Your cart <span className="font-normal text-slate-500">({totalItems})</span>
          </h2>
          <button
            type="button"
            onClick={closeCart}
            className="rounded-full p-2 text-slate-500 hover:bg-slate-100 hover:text-slate-900"
            aria-label="Close cart"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        {items.length === 0 ? (
          <div className="flex flex-1 flex-col items-center justify-center gap-3 px-6 text-center">
            <span className="flex h-14 w-14 items-center justify-center rounded-full bg-slate-100">
              <ShoppingCart className="h-6 w-6 text-slate-400" />
            </span>
            <p className="font-medium text-slate-900">Your cart is empty</p>
            <p className="text-sm text-slate-500">Browse the catalog and add something you like.</p>
            <button
              type="button"
              onClick={closeCart}
              className="mt-2 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-500"
            >
              Continue shopping
            </button>
          </div>
        ) : (
          <>
            <ul className="flex-1 divide-y divide-slate-100 overflow-y-auto px-5">
              {items.map((item) => {
                const atMax = item.maxQuantity !== undefined && item.quantity >= item.maxQuantity;
                return (
                  <li key={item.productId} className="flex gap-4 py-4">
                    <ProductImage
                      src={item.imageUrl}
                      alt={item.productName}
                      category={item.category}
                      className="h-20 w-20 shrink-0 rounded-lg"
                      sizes="80px"
                    />
                    <div className="flex min-w-0 flex-1 flex-col">
                      <div className="flex items-start justify-between gap-2">
                        <p className="line-clamp-2 text-sm font-medium text-slate-900">{item.productName}</p>
                        <button
                          type="button"
                          onClick={() => removeItem(item.productId)}
                          className="rounded p-1 text-slate-400 hover:bg-rose-50 hover:text-rose-600"
                          aria-label={`Remove ${item.productName}`}
                        >
                          <Trash2 className="h-4 w-4" />
                        </button>
                      </div>
                      <p className="text-xs text-slate-500">{formatPrice(item.unitPrice)} each</p>
                      <div className="mt-auto flex items-center justify-between pt-2">
                        <div className="inline-flex items-center rounded-lg border border-slate-200">
                          <button
                            type="button"
                            onClick={() => setQuantity(item.productId, item.quantity - 1)}
                            className="p-1.5 text-slate-600 hover:bg-slate-50"
                            aria-label={`Decrease quantity of ${item.productName}`}
                          >
                            <Minus className="h-3.5 w-3.5" />
                          </button>
                          <span className="w-8 text-center text-sm font-medium tabular-nums" aria-live="polite">
                            {item.quantity}
                          </span>
                          <button
                            type="button"
                            onClick={() => setQuantity(item.productId, item.quantity + 1)}
                            disabled={atMax}
                            className="p-1.5 text-slate-600 hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-40"
                            aria-label={`Increase quantity of ${item.productName}`}
                            title={atMax ? "No more units available" : undefined}
                          >
                            <Plus className="h-3.5 w-3.5" />
                          </button>
                        </div>
                        <p className="text-sm font-semibold text-slate-900 tabular-nums">
                          {formatPrice(item.unitPrice * item.quantity)}
                        </p>
                      </div>
                    </div>
                  </li>
                );
              })}
            </ul>

            <div className="space-y-3 border-t border-slate-200 px-5 py-5">
              {syncError && (
                <p className="rounded-lg bg-amber-50 px-3 py-2 text-xs text-amber-800">{syncError}</p>
              )}
              <div className="flex items-center justify-between text-base">
                <span className="text-slate-600">Subtotal</span>
                <span className="font-semibold text-slate-900 tabular-nums">{formatPrice(subtotal)}</span>
              </div>
              {user ? (
                <button
                  type="button"
                  onClick={() => {
                    closeCart();
                    setCheckoutOpen(true);
                  }}
                  className="w-full rounded-lg bg-indigo-600 py-3 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500"
                >
                  Checkout
                </button>
              ) : (
                <Link
                  href={`/login?next=${encodeURIComponent(pathname)}`}
                  onClick={closeCart}
                  className="block w-full rounded-lg bg-indigo-600 py-3 text-center text-sm font-semibold text-white shadow-sm hover:bg-indigo-500"
                >
                  Sign in to check out
                </Link>
              )}
              <p className="text-center text-xs text-slate-500">
                {user ? "Your cart is saved to your account." : "Your cart is kept on this device until you sign in."}
              </p>
            </div>
          </>
        )}
      </aside>

      {checkoutOpen && <CheckoutModal onClose={() => setCheckoutOpen(false)} />}
    </>
  );
}
