"use client";

import React, { useState } from "react";
import { useCart, CartItem } from "@/context/CartContext";
import { useAuth } from "@/context/AuthContext";
import { 
  X, 
  Trash2, 
  Plus, 
  Minus, 
  ShoppingBag, 
  Package, 
  Laptop, 
  ArrowRight, 
  RefreshCw,
  AlertCircle,
  User,
  ShieldCheck
} from "lucide-react";

interface CartDrawerProps {
  onProceedToCheckout: () => void;
}

export const CartDrawer: React.FC<CartDrawerProps> = ({ onProceedToCheckout }) => {
  const { 
    items, 
    totalItems, 
    subtotal, 
    isCartOpen, 
    closeCart, 
    updateQuantity, 
    removeFromCart, 
    clearCart, 
    syncing 
  } = useCart();
  const { user, loginAsCustomer, loginAsAdmin, loading: authLoading } = useAuth();
  const [imageErrors, setImageErrors] = useState<Record<string, boolean>>({});

  const handleImageError = (productId: string) => {
    setImageErrors((prev) => ({ ...prev, [productId]: true }));
  };

  if (!isCartOpen) return null;

  const isTechCategory = (category?: string) => {
    if (!category) return false;
    const c = category.toLowerCase();
    return c.includes("laptop") || c.includes("computer") || c.includes("electronic") || c.includes("tech");
  };

  const handleCheckoutClick = () => {
    if (!user) {
      // Handled by inline prompt in drawer
      return;
    }
    closeCart();
    onProceedToCheckout();
  };

  return (
    <div className="fixed inset-0 z-50 overflow-hidden">
      {/* Backdrop */}
      <div 
        onClick={closeCart}
        className="absolute inset-0 bg-slate-900/40 backdrop-blur-xs transition-opacity duration-300 animate-in fade-in"
      />

      <div className="fixed inset-y-0 right-0 max-w-full flex pl-10">
        <div className="w-screen max-w-md bg-white dark:bg-slate-900 shadow-2xl flex flex-col border-l border-slate-200 dark:border-slate-800 animate-in slide-in-from-right duration-300">
          
          {/* Header */}
          <div className="flex items-center justify-between px-6 py-5 border-b border-slate-200 dark:border-slate-800 bg-slate-50/70 dark:bg-slate-900/80">
            <div className="flex items-center gap-2.5">
              <div className="h-8 w-8 rounded-lg bg-indigo-50 dark:bg-indigo-950 flex items-center justify-center text-indigo-600 dark:text-indigo-400">
                <ShoppingBag className="h-4 w-4" />
              </div>
              <div>
                <h2 className="text-base font-bold text-slate-900 dark:text-white flex items-center gap-2">
                  <span>Shopping Cart</span>
                  {totalItems > 0 && (
                    <span className="rounded-full bg-indigo-100 dark:bg-indigo-900/60 text-indigo-700 dark:text-indigo-300 px-2 py-0.5 text-xs font-semibold">
                      {totalItems}
                    </span>
                  )}
                </h2>
                <div className="flex items-center gap-2 text-[11px] text-slate-500 dark:text-slate-400">
                  <span>Redis Live Sync</span>
                  {syncing && (
                    <span className="inline-flex items-center gap-1 text-indigo-600 dark:text-indigo-400">
                      <RefreshCw className="h-3 w-3 animate-spin" />
                      <span>Syncing...</span>
                    </span>
                  )}
                </div>
              </div>
            </div>

            <button
              onClick={closeCart}
              className="rounded-lg p-1.5 text-slate-400 hover:text-slate-600 hover:bg-slate-100 dark:hover:bg-slate-800 dark:hover:text-slate-200 transition"
              aria-label="Close cart"
            >
              <X className="h-5 w-5" />
            </button>
          </div>

          {/* Cart Item List / Empty State */}
          <div className="flex-1 overflow-y-auto p-6 space-y-4">
            {items.length === 0 ? (
              <div className="h-full flex flex-col items-center justify-center text-center py-12">
                <div className="h-20 w-20 rounded-full bg-slate-100 dark:bg-slate-800 flex items-center justify-center text-slate-400 mb-4">
                  <ShoppingBag className="h-10 w-10 stroke-[1.25]" />
                </div>
                <h3 className="text-base font-semibold text-slate-900 dark:text-white">
                  Your cart is empty
                </h3>
                <p className="mt-1 text-xs text-slate-500 dark:text-slate-400 max-w-xs">
                  Browse the live catalog and add microservices platform products to your Redis cart.
                </p>
                <button
                  onClick={closeCart}
                  className="mt-6 inline-flex items-center gap-2 rounded-xl bg-indigo-600 px-5 py-2.5 text-xs font-semibold text-white shadow-sm hover:bg-indigo-500 transition"
                >
                  <span>Browse Products</span>
                  <ArrowRight className="h-3.5 w-3.5" />
                </button>
              </div>
            ) : (
              <div className="space-y-3">
                {items.map((item) => {
                  const lineTotal = item.unitPrice * item.quantity;
                  const hasError = imageErrors[item.productId];
                  const hasValidImage =
                    !hasError &&
                    Boolean(item.imageUrl) &&
                    typeof item.imageUrl === "string" &&
                    (item.imageUrl.startsWith("http://") ||
                      item.imageUrl.startsWith("https://") ||
                      item.imageUrl.startsWith("data:image/"));

                  return (
                    <div
                      key={item.productId}
                      className="flex gap-3.5 p-3.5 rounded-xl border border-slate-200/80 bg-slate-50/50 dark:border-slate-800 dark:bg-slate-800/40"
                    >
                      {/* Thumbnail with robust error fallback */}
                      <div className="h-18 w-18 shrink-0 rounded-lg overflow-hidden bg-slate-100 dark:bg-slate-800 border border-slate-200 dark:border-slate-700 flex items-center justify-center text-slate-400">
                        {hasValidImage ? (
                          // eslint-disable-next-line @next/next/no-img-element
                          <img
                            src={item.imageUrl}
                            alt={item.productName}
                            onError={() => handleImageError(item.productId)}
                            className="h-full w-full object-cover"
                          />
                        ) : (
                          <div className="h-full w-full bg-slate-100 dark:bg-slate-800 rounded-lg flex items-center justify-center text-slate-400">
                            {isTechCategory(item.category) ? (
                              <Laptop className="h-7 w-7 text-slate-400 stroke-[1.5]" />
                            ) : (
                              <Package className="h-7 w-7 text-slate-400 stroke-[1.5]" />
                            )}
                          </div>
                        )}
                      </div>

                      {/* Info & Controls */}
                      <div className="flex-1 min-w-0 flex flex-col justify-between">
                        <div>
                          <div className="flex items-start justify-between gap-2">
                            <h4 className="text-xs font-semibold text-slate-900 dark:text-white truncate" title={item.productName}>
                              {item.productName}
                            </h4>
                            <button
                              onClick={() => removeFromCart(item.productId)}
                              className="text-slate-400 hover:text-rose-500 transition"
                              title="Remove item"
                            >
                              <Trash2 className="h-3.5 w-3.5" />
                            </button>
                          </div>
                          <p className="text-[11px] text-slate-500 dark:text-slate-400 mt-0.5">
                            ${item.unitPrice.toFixed(2)} each
                          </p>
                        </div>

                        <div className="flex items-center justify-between pt-2">
                          {/* Quantity Selector */}
                          <div className="flex items-center border border-slate-300 dark:border-slate-700 rounded-lg bg-white dark:bg-slate-800">
                            <button
                              onClick={() => updateQuantity(item.productId, item.quantity - 1)}
                              className="p-1 text-slate-500 hover:text-slate-900 dark:text-slate-400 dark:hover:text-white transition"
                              aria-label="Decrease quantity"
                            >
                              <Minus className="h-3 w-3" />
                            </button>
                            <span className="w-8 text-center text-xs font-semibold text-slate-800 dark:text-slate-200">
                              {item.quantity}
                            </span>
                            <button
                              onClick={() => updateQuantity(item.productId, item.quantity + 1)}
                              className="p-1 text-slate-500 hover:text-slate-900 dark:text-slate-400 dark:hover:text-white transition"
                              aria-label="Increase quantity"
                            >
                              <Plus className="h-3 w-3" />
                            </button>
                          </div>

                          {/* Line Total */}
                          <span className="text-xs font-bold text-slate-900 dark:text-white">
                            ${lineTotal.toFixed(2)}
                          </span>
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </div>

          {/* Footer & Checkout Prompt */}
          {items.length > 0 && (
            <div className="border-t border-slate-200 dark:border-slate-800 p-6 space-y-4 bg-slate-50/70 dark:bg-slate-900/80">
              {/* Subtotal */}
              <div className="space-y-1.5 text-xs">
                <div className="flex items-center justify-between text-slate-500 dark:text-slate-400">
                  <span>Subtotal</span>
                  <span className="font-semibold text-slate-900 dark:text-white">${subtotal.toFixed(2)}</span>
                </div>
                <div className="flex items-center justify-between text-slate-500 dark:text-slate-400">
                  <span>Estimated Shipping</span>
                  <span className="font-semibold text-emerald-600 dark:text-emerald-400">FREE</span>
                </div>
                <div className="border-t border-slate-200 dark:border-slate-800 pt-2 flex items-center justify-between text-sm font-bold text-slate-900 dark:text-white">
                  <span>Total Amount</span>
                  <span className="text-base text-indigo-600 dark:text-indigo-400">${subtotal.toFixed(2)}</span>
                </div>
              </div>

              {/* Guest Authentication Banner */}
              {!user && (
                <div className="rounded-xl border border-amber-200 bg-amber-50 p-3.5 text-xs text-amber-800 dark:border-amber-900/60 dark:bg-amber-950/40 dark:text-amber-200">
                  <div className="flex items-start gap-2">
                    <AlertCircle className="h-4 w-4 shrink-0 text-amber-600 dark:text-amber-400 mt-0.5" />
                    <div>
                      <p className="font-semibold">Sign in required for checkout</p>
                      <p className="mt-0.5 text-[11px] text-amber-700 dark:text-amber-300">
                        Orders require verified customer identity tokens.
                      </p>
                      <div className="mt-2.5 flex items-center gap-2">
                        <button
                          onClick={() => loginAsCustomer()}
                          disabled={authLoading}
                          className="inline-flex items-center gap-1 rounded-lg bg-blue-600 px-2.5 py-1 text-[11px] font-semibold text-white shadow hover:bg-blue-500 transition"
                        >
                          <User className="h-3 w-3" />
                          <span>Login Customer</span>
                        </button>
                        <button
                          onClick={() => loginAsAdmin()}
                          disabled={authLoading}
                          className="inline-flex items-center gap-1 rounded-lg bg-purple-600 px-2.5 py-1 text-[11px] font-semibold text-white shadow hover:bg-purple-500 transition"
                        >
                          <ShieldCheck className="h-3 w-3" />
                          <span>Login Admin</span>
                        </button>
                      </div>
                    </div>
                  </div>
                </div>
              )}

              {/* Action Buttons */}
              <div className="flex items-center gap-2">
                <button
                  onClick={clearCart}
                  disabled={syncing}
                  className="px-3 py-2.5 rounded-xl border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-800 text-xs font-semibold text-slate-700 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-700 transition"
                  title="Empty Cart"
                >
                  Clear
                </button>

                <button
                  onClick={handleCheckoutClick}
                  disabled={!user || syncing}
                  className={`flex-1 inline-flex items-center justify-center gap-2 rounded-xl py-3 px-4 text-xs font-bold text-white shadow-sm transition ${
                    !user
                      ? "bg-slate-300 dark:bg-slate-800 text-slate-500 cursor-not-allowed"
                      : "bg-indigo-600 hover:bg-indigo-500 active:bg-indigo-700 shadow-indigo-600/20"
                  }`}
                >
                  <span>{user ? "Proceed to Checkout" : "Login to Checkout"}</span>
                  <ArrowRight className="h-4 w-4" />
                </button>
              </div>

            </div>
          )}

        </div>
      </div>
    </div>
  );
};

export default CartDrawer;

