"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { CircleCheck, CircleX, Clock, CreditCard, Loader2, Lock, X } from "lucide-react";
import { useCart, type CartItem } from "@/context/CartContext";
import { getErrorMessage } from "@/lib/api";
import { formatPrice, shortOrderId } from "@/lib/format";
import {
  PAYMENT_OPTIONS,
  cancellationMessage,
  createOrder,
  displayStatus,
  waitForOrderOutcome,
  type Order,
  type PaymentOptionId,
  type ShippingAddress,
} from "@/lib/orders";
import { cn } from "@/lib/utils";

type Step =
  | { kind: "form"; error?: string }
  | { kind: "processing"; message: string }
  | { kind: "placed"; order: Order; items: CartItem[] }
  | { kind: "declined"; reason: string }
  | { kind: "unavailable"; reason: string }
  | { kind: "pending"; order: Order };

const EMPTY_ADDRESS: ShippingAddress = { street: "", city: "", state: "", zipCode: "", country: "United States" };

const FIELDS: { key: keyof ShippingAddress; label: string; autoComplete: string; span?: boolean }[] = [
  { key: "street", label: "Street address", autoComplete: "street-address", span: true },
  { key: "city", label: "City", autoComplete: "address-level2" },
  { key: "state", label: "State / Province", autoComplete: "address-level1" },
  { key: "zipCode", label: "ZIP / Postal code", autoComplete: "postal-code" },
  { key: "country", label: "Country", autoComplete: "country-name" },
];

function validate(address: ShippingAddress): Partial<Record<keyof ShippingAddress, string>> {
  const errors: Partial<Record<keyof ShippingAddress, string>> = {};
  for (const f of FIELDS) {
    if (!address[f.key].trim()) errors[f.key] = `${f.label} is required.`;
  }
  if (address.zipCode.trim() && !/^[A-Za-z0-9 -]{3,10}$/.test(address.zipCode.trim())) {
    errors.zipCode = "Enter a valid ZIP or postal code.";
  }
  return errors;
}

export function CheckoutModal({ onClose }: { onClose: () => void }) {
  const { items, subtotal, clearCart } = useCart();
  const [address, setAddress] = useState<ShippingAddress>(EMPTY_ADDRESS);
  const [touched, setTouched] = useState(false);
  const [paymentId, setPaymentId] = useState<PaymentOptionId>("standard");
  const [step, setStep] = useState<Step>({ kind: "form" });

  const busy = step.kind === "processing";
  const errors = validate(address);
  const hasErrors = Object.keys(errors).length > 0;

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && !busy && onClose();
    const previousOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    window.addEventListener("keydown", onKey);
    return () => {
      document.body.style.overflow = previousOverflow;
      window.removeEventListener("keydown", onKey);
    };
  }, [busy, onClose]);

  async function placeOrder(e: React.FormEvent) {
    e.preventDefault();
    setTouched(true);
    if (hasErrors || items.length === 0) return;

    const snapshot = items;
    const option = PAYMENT_OPTIONS.find((p) => p.id === paymentId)!;
    const cleanAddress = Object.fromEntries(
      Object.entries(address).map(([k, v]) => [k, v.trim()])
    ) as unknown as ShippingAddress;

    let order: Order;
    try {
      setStep({ kind: "processing", message: "Placing your order…" });
      order = await createOrder(
        cleanAddress,
        snapshot.map(({ productId, productName, unitPrice, quantity }) => ({ productId, productName, unitPrice, quantity })),
        option.token
      );
    } catch (error) {
      setStep({ kind: "form", error: getErrorMessage(error, "We couldn't place your order. Please try again.") });
      return;
    }

    // The order service reserves stock and charges the card in the background;
    // follow the order until it is confirmed or cancelled.
    try {
      setStep({ kind: "processing", message: "Reserving your items and confirming payment…" });
      const settled = await waitForOrderOutcome(order.id);
      const status = displayStatus(settled);
      if (status === "Order Placed") {
        await clearCart();
        setStep({ kind: "placed", order: settled, items: snapshot });
      } else if (status === "Payment Declined") {
        setStep({ kind: "declined", reason: cancellationMessage(settled) });
      } else if (status === "Cancelled") {
        setStep({ kind: "unavailable", reason: cancellationMessage(settled) });
      } else {
        await clearCart();
        setStep({ kind: "pending", order: settled });
      }
    } catch {
      // The order exists; only the status check failed.
      await clearCart();
      setStep({ kind: "pending", order });
    }
  }

  return (
    <div className="fixed inset-0 z-[60] flex items-end justify-center bg-slate-900/50 p-0 sm:items-center sm:p-4">
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby="checkout-title"
        className="relative flex max-h-[95vh] w-full max-w-3xl flex-col overflow-hidden rounded-t-2xl bg-white shadow-2xl sm:rounded-2xl"
      >
        <div className="flex items-center justify-between border-b border-slate-200 px-6 py-4">
          <h2 id="checkout-title" className="text-lg font-semibold text-slate-900">
            {step.kind === "placed" ? "Order confirmation" : "Checkout"}
          </h2>
          <button
            type="button"
            onClick={onClose}
            disabled={busy}
            className="rounded-full p-2 text-slate-500 hover:bg-slate-100 disabled:opacity-40"
            aria-label="Close checkout"
          >
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="overflow-y-auto">
          {step.kind === "processing" && (
            <div className="flex flex-col items-center gap-4 px-6 py-20 text-center" role="status" aria-live="polite">
              <Loader2 className="h-10 w-10 animate-spin text-indigo-600" />
              <p className="text-lg font-semibold text-slate-900">Processing</p>
              <p className="text-sm text-slate-500">{step.message} Please don&apos;t close this window.</p>
            </div>
          )}

          {step.kind === "placed" && (
            <div className="px-6 py-8">
              <div className="flex flex-col items-center text-center">
                <CircleCheck className="h-14 w-14 text-emerald-500" />
                <p className="mt-3 text-2xl font-bold text-slate-900">Order placed</p>
                <p className="mt-1 text-sm text-slate-500">
                  Thank you! Your payment was approved and your order is confirmed.
                </p>
              </div>
              <dl className="mx-auto mt-6 grid max-w-md grid-cols-2 gap-y-2 rounded-xl bg-slate-50 p-4 text-sm">
                <dt className="text-slate-500">Order number</dt>
                <dd className="text-right font-mono font-semibold text-slate-900">#{shortOrderId(step.order.id)}</dd>
                <dt className="text-slate-500">Total charged</dt>
                <dd className="text-right font-semibold text-slate-900">{formatPrice(step.order.totalAmount)}</dd>
                <dt className="text-slate-500">Ship to</dt>
                <dd className="text-right text-slate-900">
                  {step.order.shippingAddress.street}, {step.order.shippingAddress.city}
                </dd>
              </dl>
              <ul className="mx-auto mt-4 max-w-md divide-y divide-slate-100 text-sm">
                {step.items.map((i) => (
                  <li key={i.productId} className="flex justify-between py-2">
                    <span className="text-slate-700">
                      {i.quantity} × {i.productName}
                    </span>
                    <span className="tabular-nums text-slate-900">{formatPrice(i.unitPrice * i.quantity)}</span>
                  </li>
                ))}
              </ul>
              <div className="mx-auto mt-6 flex max-w-md flex-col gap-2 sm:flex-row">
                <Link
                  href="/orders"
                  onClick={onClose}
                  className="flex-1 rounded-lg bg-indigo-600 py-2.5 text-center text-sm font-semibold text-white hover:bg-indigo-500"
                >
                  View my orders
                </Link>
                <button
                  type="button"
                  onClick={onClose}
                  className="flex-1 rounded-lg border border-slate-300 py-2.5 text-sm font-semibold text-slate-700 hover:bg-slate-50"
                >
                  Continue shopping
                </button>
              </div>
            </div>
          )}

          {step.kind === "declined" && (
            <div className="flex flex-col items-center px-6 py-12 text-center">
              <CircleX className="h-14 w-14 text-rose-500" />
              <p className="mt-3 text-2xl font-bold text-slate-900">Payment declined</p>
              <p className="mt-1 text-sm text-slate-600">{step.reason}</p>
              <p className="mt-1 text-sm text-slate-500">No payment was taken, and your cart has been kept.</p>
              <div className="mt-6 flex w-full max-w-sm flex-col gap-2 sm:flex-row">
                <button
                  type="button"
                  onClick={() => {
                    setPaymentId("standard");
                    setStep({ kind: "form" });
                  }}
                  className="flex-1 rounded-lg bg-indigo-600 py-2.5 text-sm font-semibold text-white hover:bg-indigo-500"
                >
                  Try a different card
                </button>
                <button
                  type="button"
                  onClick={onClose}
                  className="flex-1 rounded-lg border border-slate-300 py-2.5 text-sm font-semibold text-slate-700 hover:bg-slate-50"
                >
                  Close
                </button>
              </div>
            </div>
          )}

          {step.kind === "unavailable" && (
            <div className="flex flex-col items-center px-6 py-12 text-center">
              <CircleX className="h-14 w-14 text-amber-500" />
              <p className="mt-3 text-2xl font-bold text-slate-900">Order not completed</p>
              <p className="mt-1 text-sm text-slate-600">{step.reason}</p>
              <p className="mt-1 text-sm text-slate-500">You have not been charged. Please update your cart and try again.</p>
              <button
                type="button"
                onClick={onClose}
                className="mt-6 w-full max-w-xs rounded-lg bg-indigo-600 py-2.5 text-sm font-semibold text-white hover:bg-indigo-500"
              >
                Back to the store
              </button>
            </div>
          )}

          {step.kind === "pending" && (
            <div className="flex flex-col items-center px-6 py-12 text-center">
              <Clock className="h-14 w-14 text-amber-500" />
              <p className="mt-3 text-2xl font-bold text-slate-900">Order received</p>
              <p className="mt-1 text-sm text-slate-600">
                Order #{shortOrderId(step.order.id)} is still being processed. We&apos;ll update its status in your order history.
              </p>
              <Link
                href="/orders"
                onClick={onClose}
                className="mt-6 w-full max-w-xs rounded-lg bg-indigo-600 py-2.5 text-center text-sm font-semibold text-white hover:bg-indigo-500"
              >
                View my orders
              </Link>
            </div>
          )}

          {step.kind === "form" && (
            <form onSubmit={placeOrder} noValidate className="grid gap-8 px-6 py-6 md:grid-cols-5">
              <div className="space-y-6 md:col-span-3">
                {step.error && (
                  <p role="alert" className="rounded-lg bg-rose-50 px-4 py-3 text-sm text-rose-700">
                    {step.error}
                  </p>
                )}
                <fieldset>
                  <legend className="text-sm font-semibold text-slate-900">Shipping address</legend>
                  <div className="mt-3 grid grid-cols-2 gap-3">
                    {FIELDS.map((f) => {
                      const err = touched ? errors[f.key] : undefined;
                      return (
                        <label key={f.key} className={cn("block", f.span && "col-span-2")}>
                          <span className="text-xs font-medium text-slate-600">{f.label}</span>
                          <input
                            name={f.key}
                            value={address[f.key]}
                            autoComplete={f.autoComplete}
                            onChange={(e) => setAddress((a) => ({ ...a, [f.key]: e.target.value }))}
                            aria-invalid={!!err}
                            className={cn(
                              "mt-1 w-full rounded-lg border px-3 py-2 text-sm focus:outline-none focus:ring-2",
                              err
                                ? "border-rose-300 focus:ring-rose-100"
                                : "border-slate-300 focus:border-indigo-400 focus:ring-indigo-100"
                            )}
                          />
                          {err && <span className="mt-1 block text-xs text-rose-600">{err}</span>}
                        </label>
                      );
                    })}
                  </div>
                </fieldset>

                <fieldset>
                  <legend className="text-sm font-semibold text-slate-900">Payment method</legend>
                  <div className="mt-3 space-y-2">
                    {PAYMENT_OPTIONS.map((p) => (
                      <label
                        key={p.id}
                        className={cn(
                          "flex cursor-pointer items-center gap-3 rounded-xl border p-3 transition",
                          paymentId === p.id ? "border-indigo-500 bg-indigo-50/50 ring-1 ring-indigo-500" : "border-slate-200 hover:border-slate-300"
                        )}
                      >
                        <input
                          type="radio"
                          name="payment"
                          value={p.id}
                          checked={paymentId === p.id}
                          onChange={() => setPaymentId(p.id)}
                          className="h-4 w-4 accent-indigo-600"
                        />
                        <CreditCard className="h-5 w-5 text-slate-500" aria-hidden />
                        <span>
                          <span className="block text-sm font-medium text-slate-900">{p.label}</span>
                          <span className="block text-xs text-slate-500">{p.hint}</span>
                        </span>
                      </label>
                    ))}
                  </div>
                </fieldset>
              </div>

              <aside className="h-fit rounded-xl bg-slate-50 p-4 md:col-span-2">
                <h3 className="text-sm font-semibold text-slate-900">Order summary</h3>
                <ul className="mt-3 space-y-2 text-sm">
                  {items.map((i) => (
                    <li key={i.productId} className="flex justify-between gap-3">
                      <span className="min-w-0 truncate text-slate-600">
                        {i.quantity} × {i.productName}
                      </span>
                      <span className="shrink-0 tabular-nums text-slate-900">{formatPrice(i.unitPrice * i.quantity)}</span>
                    </li>
                  ))}
                </ul>
                <div className="mt-4 flex justify-between border-t border-slate-200 pt-3 text-base font-semibold text-slate-900">
                  <span>Total</span>
                  <span className="tabular-nums">{formatPrice(subtotal)}</span>
                </div>
                <button
                  type="submit"
                  disabled={items.length === 0}
                  className="mt-4 flex w-full items-center justify-center gap-2 rounded-lg bg-indigo-600 py-3 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 disabled:opacity-50"
                >
                  <Lock className="h-4 w-4" aria-hidden />
                  Place order · {formatPrice(subtotal)}
                </button>
                {items.length === 0 && <p className="mt-2 text-center text-xs text-slate-500">Your cart is empty.</p>}
              </aside>
            </form>
          )}
        </div>
      </div>
    </div>
  );
}
