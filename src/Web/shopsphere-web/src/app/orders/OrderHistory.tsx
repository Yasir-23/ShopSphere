"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { ChevronLeft, ChevronRight, Package, RefreshCw } from "lucide-react";
import { getErrorMessage } from "@/lib/api";
import { formatDate, formatPrice, shortOrderId } from "@/lib/format";
import { cancellationMessage, displayStatus, getMyOrders, type Order, type OrderPage } from "@/lib/orders";
import { StatusBadge } from "@/components/StatusBadge";

const PAGE_SIZE = 5;

type State =
  | { status: "loading" }
  | { status: "error"; message: string }
  | { status: "ready"; page: OrderPage };

async function loadPage(pageNumber: number) {
  return { page: await getMyOrders(pageNumber, PAGE_SIZE) };
}

function OrderCard({ order }: { order: Order }) {
  const status = displayStatus(order);
  const units = order.items.reduce((n, i) => n + i.quantity, 0);
  const a = order.shippingAddress;
  return (
    <article className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
      <header className="flex flex-wrap items-center gap-x-8 gap-y-3 border-b border-slate-100 bg-slate-50/70 px-5 py-4 text-sm">
        <div>
          <p className="text-xs text-slate-500">Order</p>
          <p className="font-mono font-semibold text-slate-900">#{shortOrderId(order.id)}</p>
        </div>
        <div>
          <p className="text-xs text-slate-500">Placed</p>
          <p className="font-medium text-slate-900">{formatDate(order.createdAtUtc)}</p>
        </div>
        <div>
          <p className="text-xs text-slate-500">Total</p>
          <p className="font-semibold text-slate-900">{formatPrice(order.totalAmount)}</p>
        </div>
        <div className="ml-auto">
          <StatusBadge status={status} />
        </div>
      </header>
      <div className="grid gap-4 px-5 py-4 sm:grid-cols-3">
        <ul className="space-y-1.5 text-sm sm:col-span-2">
          {order.items.map((i) => (
            <li key={i.id} className="flex justify-between gap-4">
              <span className="text-slate-700">
                <span className="font-medium text-slate-900">{i.quantity} ×</span> {i.productName}
              </span>
              <span className="shrink-0 tabular-nums text-slate-600">{formatPrice(i.totalPrice)}</span>
            </li>
          ))}
          <li className="pt-1 text-xs text-slate-500">
            {units} {units === 1 ? "item" : "items"}
          </li>
        </ul>
        <div className="text-sm">
          <p className="text-xs text-slate-500">Ship to</p>
          <p className="text-slate-800">{a.street}</p>
          <p className="text-slate-800">
            {a.city}, {a.state} {a.zipCode}
          </p>
          <p className="text-slate-800">{a.country}</p>
          {order.status === "Cancelled" && (
            <p className="mt-2 text-xs text-rose-600">{cancellationMessage(order)}</p>
          )}
        </div>
      </div>
    </article>
  );
}

export function OrderHistory() {
  const [request, setRequest] = useState({ pageNumber: 1, attempt: 0 });
  const [state, setState] = useState<State>({ status: "loading" });
  const { pageNumber } = request;

  useEffect(() => {
    let active = true;
    loadPage(request.pageNumber)
      .then((data) => active && setState({ status: "ready", ...data }))
      .catch((error) => active && setState({ status: "error", message: getErrorMessage(error, "We couldn't load your orders.") }));
    return () => {
      active = false;
    };
  }, [request]);

  const goTo = (n: number) => {
    setState({ status: "loading" });
    setRequest((r) => ({ pageNumber: n, attempt: r.attempt + 1 }));
    window.scrollTo({ top: 0, behavior: "smooth" });
  };

  return (
    <div className="mx-auto max-w-4xl px-4 py-10 sm:px-6 lg:px-8">
      <div className="flex items-end justify-between gap-4">
        <div>
          <h1 className="text-3xl font-bold tracking-tight text-slate-900">My orders</h1>
          <p className="mt-1 text-sm text-slate-500">
            {state.status === "ready" ? `${state.page.totalCount} ${state.page.totalCount === 1 ? "order" : "orders"}` : "Your purchase history"}
          </p>
        </div>
        <button
          type="button"
          onClick={() => goTo(pageNumber)}
          className="inline-flex items-center gap-1.5 rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
        >
          <RefreshCw className="h-4 w-4" aria-hidden /> Refresh
        </button>
      </div>

      <div className="mt-8 space-y-4">
        {state.status === "loading" &&
          Array.from({ length: 3 }, (_, i) => <div key={i} className="h-44 animate-pulse rounded-2xl bg-white ring-1 ring-slate-200" />)}

        {state.status === "error" && (
          <div className="rounded-2xl border border-slate-200 bg-white px-6 py-12 text-center">
            <p className="font-semibold text-slate-900">Your orders couldn&apos;t be loaded</p>
            <p className="mt-1 text-sm text-slate-500">{state.message}</p>
            <button
              type="button"
              onClick={() => goTo(pageNumber)}
              className="mt-5 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-500"
            >
              Try again
            </button>
          </div>
        )}

        {state.status === "ready" && state.page.items.length === 0 && (
          <div className="flex flex-col items-center rounded-2xl border border-dashed border-slate-300 bg-white px-6 py-16 text-center">
            <Package className="h-10 w-10 text-slate-300" aria-hidden />
            <p className="mt-3 font-semibold text-slate-900">No orders yet</p>
            <p className="mt-1 text-sm text-slate-500">When you place an order, it will show up here.</p>
            <Link href="/" className="mt-5 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-500">
              Start shopping
            </Link>
          </div>
        )}

        {state.status === "ready" &&
          state.page.items.map((o) => <OrderCard key={o.id} order={o} />)}
      </div>

      {state.status === "ready" && state.page.totalPages > 1 && (
        <nav className="mt-8 flex items-center justify-between" aria-label="Pagination">
          <button
            type="button"
            disabled={pageNumber <= 1}
            onClick={() => goTo(pageNumber - 1)}
            className="inline-flex items-center gap-1 rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-40"
          >
            <ChevronLeft className="h-4 w-4" aria-hidden /> Newer
          </button>
          <span className="text-sm text-slate-500">
            Page {pageNumber} of {state.page.totalPages}
          </span>
          <button
            type="button"
            disabled={pageNumber >= state.page.totalPages}
            onClick={() => goTo(pageNumber + 1)}
            className="inline-flex items-center gap-1 rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50 disabled:opacity-40"
          >
            Older <ChevronRight className="h-4 w-4" aria-hidden />
          </button>
        </nav>
      )}
    </div>
  );
}
