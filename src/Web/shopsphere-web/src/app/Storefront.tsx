"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useEffect, useMemo, useState } from "react";
import { ArrowRight, PackageSearch, RefreshCw, ShieldCheck, ShoppingBag, Truck } from "lucide-react";
import { getErrorMessage } from "@/lib/api";
import { getProducts, getStockMap, type Product, type StockMap } from "@/lib/catalog";
import { loadCategories } from "@/lib/useCategories";
import { ProductCard, ProductCardSkeleton } from "@/components/ProductCard";
import { cn } from "@/lib/utils";

type SortKey = "featured" | "price_asc" | "price_desc" | "name";

const SORTS: { value: SortKey; label: string }[] = [
  { value: "featured", label: "Newest" },
  { value: "price_asc", label: "Price: low to high" },
  { value: "price_desc", label: "Price: high to low" },
  { value: "name", label: "Name A–Z" },
];

type CatalogState =
  | { status: "loading" }
  | { status: "error"; message: string }
  | { status: "ready"; products: Product[]; categories: string[] };

async function loadCatalog() {
  const [products, categories] = await Promise.all([getProducts(), loadCategories()]);
  const names = new Set(categories.map((c) => c.name));
  products.forEach((p) => p.category && names.add(p.category));
  return { products: products.filter((p) => p.isActive !== false), categories: Array.from(names) };
}

export function Storefront() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const q = searchParams.get("q")?.trim() ?? "";
  const category = searchParams.get("category");

  const [state, setState] = useState<CatalogState>({ status: "loading" });
  const [stock, setStock] = useState<StockMap>({});
  const [sort, setSort] = useState<SortKey>("featured");

  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let active = true;
    loadCatalog()
      .then((catalog) => {
        if (!active) return;
        setState({ status: "ready", ...catalog });
        return getStockMap(catalog.products.map((p) => p.id)).then((s) => active && setStock(s));
      })
      .catch((error) => active && setState({ status: "error", message: getErrorMessage(error, "We couldn't load the catalog.") }));
    return () => {
      active = false;
    };
  }, [attempt]);

  const retry = () => {
    setState({ status: "loading" });
    setAttempt((n) => n + 1);
  };

  const setCategory = (value: string | null) => {
    const params = new URLSearchParams(searchParams.toString());
    if (value) params.set("category", value);
    else params.delete("category");
    const qs = params.toString();
    router.replace(qs ? `/?${qs}` : "/", { scroll: false });
  };

  const visible = useMemo(() => {
    if (state.status !== "ready") return [];
    const term = q.toLowerCase();
    const filtered = state.products.filter((p) => {
      if (category && p.category.toLowerCase() !== category.toLowerCase()) return false;
      if (!term) return true;
      return [p.name, p.description, p.sku, p.category].some((f) => f?.toLowerCase().includes(term));
    });
    const sorted = [...filtered];
    if (sort === "price_asc") sorted.sort((a, b) => a.price - b.price);
    if (sort === "price_desc") sorted.sort((a, b) => b.price - a.price);
    if (sort === "name") sorted.sort((a, b) => a.name.localeCompare(b.name));
    return sorted;
  }, [state, q, category, sort]);

  const countFor = (c: string | null) =>
    state.status === "ready" ? state.products.filter((p) => !c || p.category === c).length : 0;

  return (
    <>
      <section className="relative overflow-hidden bg-gradient-to-br from-indigo-700 via-indigo-600 to-violet-600 text-white">
        <div className="absolute -right-24 -top-24 h-96 w-96 rounded-full bg-white/10 blur-3xl" aria-hidden />
        <div className="relative mx-auto grid max-w-7xl items-center gap-10 px-4 py-14 sm:px-6 md:grid-cols-2 md:py-20 lg:px-8">
          <div>
            <p className="inline-flex rounded-full bg-white/15 px-3 py-1 text-xs font-semibold uppercase tracking-wider">
              New season tech
            </p>
            <h1 className="mt-4 text-4xl font-extrabold tracking-tight sm:text-5xl">
              Upgrade your desk, your sound and your home.
            </h1>
            <p className="mt-4 max-w-lg text-lg text-indigo-100">
              Hand-picked electronics, peripherals and smart home gear, with live stock levels and a quick, secure checkout.
            </p>
            <div className="mt-8 flex flex-wrap gap-3">
              <a
                href="#catalog"
                className="inline-flex items-center gap-2 rounded-lg bg-white px-5 py-3 text-sm font-semibold text-indigo-700 shadow-sm hover:bg-indigo-50"
              >
                Shop the catalog <ArrowRight className="h-4 w-4" aria-hidden />
              </a>
              <Link
                href="/orders"
                className="inline-flex items-center gap-2 rounded-lg border border-white/40 px-5 py-3 text-sm font-semibold text-white hover:bg-white/10"
              >
                Track an order
              </Link>
            </div>
          </div>
          <ul className="grid gap-3 sm:grid-cols-3 md:grid-cols-1 lg:ml-auto lg:w-80">
            {[
              { Icon: ShieldCheck, title: "Secure checkout", text: "Card payments confirmed instantly." },
              { Icon: ShoppingBag, title: "Cart that follows you", text: "Sign in and pick up where you left off." },
              { Icon: Truck, title: "Order history", text: "See every order and its status in one place." },
            ].map(({ Icon, title, text }) => (
              <li key={title} className="flex gap-3 rounded-xl bg-white/10 p-4 backdrop-blur">
                <Icon className="h-6 w-6 shrink-0 text-indigo-100" aria-hidden />
                <div>
                  <p className="font-semibold">{title}</p>
                  <p className="text-sm text-indigo-100">{text}</p>
                </div>
              </li>
            ))}
          </ul>
        </div>
      </section>

      <section id="catalog" className="mx-auto max-w-7xl px-4 py-10 sm:px-6 lg:px-8">
        <div className="flex flex-col gap-4 sm:flex-row sm:items-end sm:justify-between">
          <div>
            <h2 className="text-2xl font-bold tracking-tight text-slate-900">{category ?? "All products"}</h2>
            <p className="mt-1 text-sm text-slate-500" aria-live="polite">
              {state.status === "ready"
                ? `${visible.length} ${visible.length === 1 ? "product" : "products"}${q ? ` matching “${q}”` : ""}`
                : "Loading products…"}
            </p>
          </div>
          <label className="flex items-center gap-2 text-sm text-slate-600">
            Sort by
            <select
              value={sort}
              onChange={(e) => setSort(e.target.value as SortKey)}
              className="rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm text-slate-900 focus:border-indigo-400 focus:outline-none focus:ring-2 focus:ring-indigo-100"
            >
              {SORTS.map((s) => (
                <option key={s.value} value={s.value}>
                  {s.label}
                </option>
              ))}
            </select>
          </label>
        </div>

        {state.status === "ready" && (
          <div className="mt-6 flex flex-wrap gap-2" role="tablist" aria-label="Filter by category">
            {[null, ...state.categories].map((c) => {
              const active = (category ?? null) === c;
              return (
                <button
                  key={c ?? "all"}
                  type="button"
                  role="tab"
                  aria-selected={active}
                  onClick={() => setCategory(c)}
                  className={cn(
                    "rounded-full border px-4 py-1.5 text-sm font-medium transition",
                    active
                      ? "border-indigo-600 bg-indigo-600 text-white"
                      : "border-slate-200 bg-white text-slate-700 hover:border-slate-300"
                  )}
                >
                  {c ?? "All"} <span className={active ? "text-indigo-200" : "text-slate-400"}>{countFor(c)}</span>
                </button>
              );
            })}
          </div>
        )}

        <div className="mt-8">
          {state.status === "loading" && (
            <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
              {Array.from({ length: 8 }, (_, i) => (
                <ProductCardSkeleton key={i} />
              ))}
            </div>
          )}

          {state.status === "error" && (
            <div className="flex flex-col items-center rounded-2xl border border-slate-200 bg-white px-6 py-16 text-center">
              <p className="font-semibold text-slate-900">The catalog is unavailable right now</p>
              <p className="mt-1 text-sm text-slate-500">{state.message}</p>
              <button
                type="button"
                onClick={retry}
                className="mt-5 inline-flex items-center gap-2 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-500"
              >
                <RefreshCw className="h-4 w-4" aria-hidden /> Try again
              </button>
            </div>
          )}

          {state.status === "ready" && visible.length === 0 && (
            <div className="flex flex-col items-center rounded-2xl border border-dashed border-slate-300 bg-white px-6 py-16 text-center">
              <PackageSearch className="h-10 w-10 text-slate-300" aria-hidden />
              <p className="mt-3 font-semibold text-slate-900">No products found</p>
              <p className="mt-1 text-sm text-slate-500">Try a different search term or category.</p>
              <button
                type="button"
                onClick={() => router.replace("/", { scroll: false })}
                className="mt-5 rounded-lg border border-slate-300 px-4 py-2 text-sm font-semibold text-slate-700 hover:bg-slate-50"
              >
                Clear filters
              </button>
            </div>
          )}

          {state.status === "ready" && visible.length > 0 && (
            <div className="grid gap-6 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4">
              {visible.map((p) => (
                <ProductCard key={p.id} product={p} stock={p.id in stock ? stock[p.id] : undefined} />
              ))}
            </div>
          )}
        </div>
      </section>
    </>
  );
}
