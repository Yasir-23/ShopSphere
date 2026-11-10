"use client";

import Link from "next/link";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { useState } from "react";
import { LayoutDashboard, LogOut, Package, Search, ShoppingBag, ShoppingCart, User } from "lucide-react";
import { useAuth } from "@/context/AuthContext";
import { useCart } from "@/context/CartContext";
import { useCategories } from "@/lib/useCategories";
import { cn } from "@/lib/utils";

function buildHomeHref(q: string, category: string | null): string {
  const params = new URLSearchParams();
  if (q.trim()) params.set("q", q.trim());
  if (category) params.set("category", category);
  const qs = params.toString();
  return qs ? `/?${qs}` : "/";
}

function SearchBox({ initial, onHome, category }: { initial: string; onHome: boolean; category: string | null }) {
  const router = useRouter();
  const [value, setValue] = useState(initial);
  const [lastInitial, setLastInitial] = useState(initial);
  // Reset the box when the search is cleared elsewhere (e.g. "Clear filters").
  if (initial !== lastInitial) {
    setLastInitial(initial);
    if (initial === "") setValue("");
  }

  return (
    <form
      role="search"
      className="relative w-full"
      onSubmit={(e) => {
        e.preventDefault();
        router.push(buildHomeHref(value, onHome ? category : null) + "#catalog");
      }}
    >
      <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400" aria-hidden />
      <input
        type="search"
        value={value}
        onChange={(e) => {
          setValue(e.target.value);
          if (onHome) router.replace(buildHomeHref(e.target.value, category), { scroll: false });
        }}
        placeholder="Search products"
        aria-label="Search products"
        className="h-10 w-full rounded-full border border-slate-200 bg-slate-50 pl-9 pr-4 text-sm text-slate-900 placeholder:text-slate-400 focus:border-indigo-400 focus:bg-white focus:outline-none focus:ring-2 focus:ring-indigo-100"
      />
    </form>
  );
}

export function Navbar() {
  const { user, ready, logout } = useAuth();
  const { totalItems, openCart } = useCart();
  const categories = useCategories();
  const pathname = usePathname();
  const router = useRouter();
  const searchParams = useSearchParams();
  const onHome = pathname === "/";
  const activeCategory = onHome ? searchParams.get("category") : null;
  const q = onHome ? searchParams.get("q") ?? "" : "";

  const navLink = (href: string, label: string, Icon: typeof Package) => (
    <Link
      href={href}
      className={cn(
        "hidden items-center gap-1.5 rounded-lg px-3 py-2 text-sm font-medium transition md:inline-flex",
        pathname === href ? "bg-indigo-50 text-indigo-700" : "text-slate-600 hover:bg-slate-100 hover:text-slate-900"
      )}
    >
      <Icon className="h-4 w-4" aria-hidden />
      {label}
    </Link>
  );

  return (
    <header className="sticky top-0 z-40 border-b border-slate-200 bg-white/90 backdrop-blur">
      <div className="mx-auto flex h-16 max-w-7xl items-center gap-3 px-4 sm:gap-6 sm:px-6 lg:px-8">
        <Link href="/" className="flex shrink-0 items-center gap-2" aria-label="ShopSphere home">
          <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-indigo-600 text-white">
            <ShoppingBag className="h-5 w-5" aria-hidden />
          </span>
          <span className="hidden text-lg font-bold tracking-tight text-slate-900 sm:inline">ShopSphere</span>
        </Link>

        <div className="min-w-0 flex-1 sm:max-w-xl">
          <SearchBox key={onHome ? "home" : pathname} initial={q} onHome={onHome} category={activeCategory} />
        </div>

        <nav className="ml-auto flex items-center gap-1 sm:gap-2" aria-label="Account">
          {user && navLink("/orders", "Orders", Package)}
          {user?.isAdmin && navLink("/admin", "Admin", LayoutDashboard)}

          {!ready ? (
            <span className="h-9 w-20 animate-pulse rounded-lg bg-slate-100" aria-hidden />
          ) : user ? (
            <div className="flex items-center gap-2">
              <div className="hidden items-center gap-2 rounded-full border border-slate-200 py-1 pl-1 pr-3 lg:flex">
                <span className="flex h-7 w-7 items-center justify-center rounded-full bg-indigo-100 text-xs font-bold uppercase text-indigo-700">
                  {(user.firstName || user.email)[0]}
                </span>
                <span className="max-w-32 truncate text-sm font-medium text-slate-800">
                  {user.firstName || user.email}
                </span>
                <span
                  className={cn(
                    "rounded-full px-2 py-0.5 text-[11px] font-semibold",
                    user.isAdmin ? "bg-violet-100 text-violet-700" : "bg-slate-100 text-slate-600"
                  )}
                >
                  {user.isAdmin ? "Admin" : "Customer"}
                </span>
              </div>
              <button
                type="button"
                onClick={() => {
                  logout();
                  router.push("/");
                }}
                className="inline-flex items-center gap-1.5 rounded-lg px-2.5 py-2 text-sm font-medium text-slate-600 transition hover:bg-slate-100 hover:text-slate-900"
                title="Sign out"
              >
                <LogOut className="h-4 w-4" aria-hidden />
                <span className="hidden sm:inline">Sign out</span>
              </button>
            </div>
          ) : (
            <Link
              href={pathname === "/login" || pathname === "/register" ? "/login" : `/login?next=${encodeURIComponent(pathname)}`}
              className="inline-flex items-center gap-1.5 rounded-lg px-3 py-2 text-sm font-medium text-slate-700 transition hover:bg-slate-100"
            >
              <User className="h-4 w-4" aria-hidden />
              Sign in
            </Link>
          )}

          <button
            type="button"
            onClick={openCart}
            className="relative inline-flex h-10 w-10 items-center justify-center rounded-full text-slate-700 transition hover:bg-slate-100"
            aria-label={`Open cart, ${totalItems} item${totalItems === 1 ? "" : "s"}`}
          >
            <ShoppingCart className="h-5 w-5" aria-hidden />
            {totalItems > 0 && (
              <span className="absolute -right-0.5 -top-0.5 flex h-5 min-w-5 items-center justify-center rounded-full bg-indigo-600 px-1 text-[11px] font-bold text-white">
                {totalItems > 99 ? "99+" : totalItems}
              </span>
            )}
          </button>
        </nav>
      </div>

      <nav aria-label="Categories" className="border-t border-slate-100">
        <div className="mx-auto flex max-w-7xl gap-1 overflow-x-auto px-4 py-2 sm:px-6 lg:px-8">
          {[{ id: "all", name: "All products" }, ...categories].map((c) => {
            const value = c.id === "all" ? null : c.name;
            const active = onHome && activeCategory === value;
            return (
              <Link
                key={c.id}
                href={buildHomeHref(q, value) + "#catalog"}
                scroll={false}
                className={cn(
                  "whitespace-nowrap rounded-full px-3 py-1 text-sm transition",
                  active ? "bg-slate-900 font-medium text-white" : "text-slate-600 hover:bg-slate-100 hover:text-slate-900"
                )}
              >
                {c.name}
              </Link>
            );
          })}
          {user && (
            <Link
              href="/orders"
              className="whitespace-nowrap rounded-full px-3 py-1 text-sm text-slate-600 hover:bg-slate-100 md:hidden"
            >
              My orders
            </Link>
          )}
          {user?.isAdmin && (
            <Link
              href="/admin"
              className="whitespace-nowrap rounded-full px-3 py-1 text-sm text-slate-600 hover:bg-slate-100 md:hidden"
            >
              Admin
            </Link>
          )}
        </div>
      </nav>
    </header>
  );
}

export function NavbarFallback() {
  return <div className="sticky top-0 z-40 h-[105px] border-b border-slate-200 bg-white" aria-hidden />;
}
