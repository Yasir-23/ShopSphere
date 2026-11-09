"use client";

import React, { useEffect, useState, useCallback } from "react";
import { getProducts, getStock, Product, InventoryStock } from "@/lib/catalog";
import { 
  Package, 
  Laptop,
  ShoppingCart, 
  CheckCircle2, 
  XCircle, 
  Loader2, 
  RefreshCw, 
  Tag, 
  Info,
  Sparkles
} from "lucide-react";

export const ProductGrid: React.FC = () => {
  const [products, setProducts] = useState<Product[]>([]);
  const [stockMap, setStockMap] = useState<Record<string, InventoryStock | null>>({});
  const [loading, setLoading] = useState<boolean>(true);
  const [stockLoading, setStockLoading] = useState<Record<string, boolean>>({});
  const [imageErrors, setImageErrors] = useState<Record<string, boolean>>({});
  const [error, setError] = useState<string | null>(null);
  const [cartNotification, setCartNotification] = useState<string | null>(null);

  const fetchProductsAndStock = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const items = await getProducts(1, 24);
      setProducts(items);

      // Concurrently query live inventory stock for each product
      const initialStockLoading: Record<string, boolean> = {};
      items.forEach((p) => {
        initialStockLoading[p.id] = true;
      });
      setStockLoading(initialStockLoading);

      // Query stock per product via gateway
      await Promise.all(
        items.map(async (product) => {
          try {
            const stock = await getStock(product.id);
            setStockMap((prev) => ({ ...prev, [product.id]: stock }));
          } catch {
            setStockMap((prev) => ({ ...prev, [product.id]: null }));
          } finally {
            setStockLoading((prev) => ({ ...prev, [product.id]: false }));
          }
        })
      );
    } catch (err: any) {
      console.error("Error fetching live catalog:", err);
      setError("Unable to load catalog from http://localhost:5001. Please ensure the Gateway and microservices are running.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    fetchProductsAndStock();
  }, [fetchProductsAndStock]);

  const handleAddToCart = (product: Product, availableUnits: number) => {
    if (availableUnits <= 0) return;
    setCartNotification(`Added "${product.name}" to basket! (Available: ${availableUnits} units)`);
    setTimeout(() => {
      setCartNotification(null);
    }, 3500);
  };

  const handleImageError = (productId: string) => {
    setImageErrors((prev) => ({ ...prev, [productId]: true }));
  };

  const isCategoryTech = (category: string) => {
    const c = category.toLowerCase();
    return c.includes("laptop") || c.includes("computer") || c.includes("electronic") || c.includes("tech");
  };

  return (
    <div className="space-y-6">
      {/* Header & Controls */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4 pb-2 border-b border-slate-200 dark:border-slate-800">
        <div>
          <div className="flex items-center gap-2">
            <h2 className="text-2xl font-bold tracking-tight text-slate-900 dark:text-white">
              Live Product Catalog
            </h2>
            <span className="flex items-center gap-1 rounded-full bg-emerald-50 px-2.5 py-0.5 text-xs font-semibold text-emerald-700 border border-emerald-200 dark:bg-emerald-950/60 dark:text-emerald-300 dark:border-emerald-800">
              <Sparkles className="h-3 w-3" />
              MongoDB + Postgres
            </span>
          </div>
          <p className="text-sm text-slate-500 dark:text-slate-400 mt-0.5">
            Real-time catalog synchronized with live Inventory Service stock levels.
          </p>
        </div>

        <button
          onClick={fetchProductsAndStock}
          disabled={loading}
          className="inline-flex items-center gap-2 self-start sm:self-auto rounded-lg border border-slate-300 bg-white px-3.5 py-2 text-xs font-medium text-slate-700 shadow-sm hover:bg-slate-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-indigo-500 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-200 dark:hover:bg-slate-700 transition"
        >
          <RefreshCw className={`h-3.5 w-3.5 text-slate-500 ${loading ? "animate-spin" : ""}`} />
          <span>Refresh Catalog & Stock</span>
        </button>
      </div>

      {/* Cart Feedback Notification */}
      {cartNotification && (
        <div className="animate-in fade-in slide-in-from-top-2 duration-200 flex items-center justify-between rounded-xl border border-emerald-200 bg-emerald-50 px-4 py-3 text-sm font-medium text-emerald-800 dark:border-emerald-900 dark:bg-emerald-950/80 dark:text-emerald-200 shadow-sm">
          <div className="flex items-center gap-2.5">
            <CheckCircle2 className="h-5 w-5 text-emerald-600 dark:text-emerald-400 shrink-0" />
            <span>{cartNotification}</span>
          </div>
          <button
            onClick={() => setCartNotification(null)}
            className="text-xs text-emerald-600 hover:text-emerald-800 dark:text-emerald-400 font-semibold"
          >
            Close
          </button>
        </div>
      )}

      {/* Error state */}
      {error && (
        <div className="rounded-xl border border-rose-200 bg-rose-50 p-6 text-center text-rose-800 dark:border-rose-900/60 dark:bg-rose-950/40 dark:text-rose-200 shadow-sm">
          <Info className="mx-auto h-8 w-8 text-rose-500 mb-2" />
          <h3 className="text-base font-semibold">Catalog Connection Error</h3>
          <p className="mt-1 text-sm text-rose-600 dark:text-rose-300 max-w-lg mx-auto">
            {error}
          </p>
          <button
            onClick={fetchProductsAndStock}
            className="mt-4 inline-flex items-center gap-2 rounded-lg bg-rose-600 px-4 py-2 text-xs font-semibold text-white shadow hover:bg-rose-500 transition"
          >
            <RefreshCw className="h-3.5 w-3.5" />
            Retry Connection
          </button>
        </div>
      )}

      {/* Loading state */}
      {loading && products.length === 0 && (
        <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
          {[...Array(8)].map((_, idx) => (
            <div
              key={idx}
              className="animate-pulse rounded-2xl border border-slate-200 bg-white p-5 shadow-sm dark:border-slate-800 dark:bg-slate-900"
            >
              <div className="h-48 w-full rounded-xl bg-slate-100 dark:bg-slate-800 mb-4" />
              <div className="h-4 w-3/4 rounded bg-slate-200 dark:bg-slate-700 mb-2" />
              <div className="h-3 w-1/2 rounded bg-slate-100 dark:bg-slate-800 mb-4" />
              <div className="h-6 w-1/3 rounded bg-slate-200 dark:bg-slate-700 mb-4" />
              <div className="h-10 w-full rounded-lg bg-slate-100 dark:bg-slate-800" />
            </div>
          ))}
        </div>
      )}

      {/* Empty state */}
      {!loading && products.length === 0 && !error && (
        <div className="rounded-2xl border-2 border-dashed border-slate-200 p-12 text-center dark:border-slate-800">
          <Package className="mx-auto h-12 w-12 text-slate-400 mb-3" />
          <h3 className="text-base font-medium text-slate-900 dark:text-white">No products found</h3>
          <p className="mt-1 text-sm text-slate-500 dark:text-slate-400">
            The Catalog database appears empty or seeder has not run yet.
          </p>
          <button
            onClick={fetchProductsAndStock}
            className="mt-4 inline-flex items-center gap-2 rounded-lg bg-indigo-600 px-4 py-2 text-xs font-semibold text-white shadow hover:bg-indigo-500 transition"
          >
            <RefreshCw className="h-3.5 w-3.5" />
            Reload
          </button>
        </div>
      )}

      {/* Products Grid */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-6">
        {products.map((product) => {
          const stock = stockMap[product.id];
          const isCheckingStock = stockLoading[product.id];
          
          // Determine available quantity from live inventory stock response
          const availableUnits = 
            stock !== null && stock !== undefined
              ? (stock.quantityAvailable ?? stock.availableQuantity ?? stock.quantityOnHand - stock.quantityReserved)
              : (product.isAvailable ? 10 : 0);

          const isOutOfStock = availableUnits <= 0;

          // Check if image should render or fall back
          const hasImageError = imageErrors[product.id];
          const hasValidImage = 
            !hasImageError && 
            Boolean(product.imageUrl) && 
            typeof product.imageUrl === "string" &&
            (product.imageUrl.startsWith("http://") || 
             product.imageUrl.startsWith("https://") || 
             product.imageUrl.startsWith("data:image/"));

          return (
            <div
              key={product.id}
              className="group flex flex-col justify-between rounded-2xl border border-slate-200/80 bg-white p-5 shadow-sm transition-all duration-200 hover:shadow-md hover:border-slate-300 dark:border-slate-800 dark:bg-slate-900 dark:hover:border-slate-700"
            >
              <div>
                {/* Product Image / Visual Fallback Thumbnail */}
                <div className="relative mb-4 overflow-hidden rounded-xl border border-slate-100 dark:border-slate-800">
                  {hasValidImage ? (
                    <div className="relative h-48 w-full bg-slate-50 dark:bg-slate-800/50">
                      {/* eslint-disable-next-line @next/next/no-img-element */}
                      <img
                        src={product.imageUrl}
                        alt={product.name}
                        onError={() => handleImageError(product.id)}
                        className="h-48 w-full object-cover transition duration-300 group-hover:scale-105"
                      />
                    </div>
                  ) : (
                    <div className="h-48 w-full bg-slate-100 dark:bg-slate-800 rounded-t-xl flex flex-col items-center justify-center text-slate-400 transition duration-200 group-hover:bg-slate-200/60 dark:group-hover:bg-slate-800/80">
                      {isCategoryTech(product.category) ? (
                        <Laptop className="w-12 h-12 stroke-[1.5]" />
                      ) : (
                        <Package className="w-12 h-12 stroke-[1.5]" />
                      )}
                      <span className="text-[11px] font-semibold tracking-wider uppercase mt-2 text-slate-400 dark:text-slate-500">
                        {product.sku || product.category}
                      </span>
                    </div>
                  )}

                  {/* Category Pill on Image */}
                  <span className="absolute top-3 left-3 inline-flex items-center gap-1 rounded-md bg-white/90 px-2 py-0.5 text-[11px] font-semibold text-slate-700 shadow-sm backdrop-blur dark:bg-slate-900/90 dark:text-slate-300">
                    <Tag className="h-3 w-3 text-indigo-500" />
                    {product.category}
                  </span>
                </div>

                {/* Name & Description */}
                <h3 className="text-base font-semibold text-slate-900 line-clamp-1 dark:text-white" title={product.name}>
                  {product.name}
                </h3>
                <p className="mt-1 text-xs text-slate-500 line-clamp-2 dark:text-slate-400" title={product.description}>
                  {product.description || "High-performance microservices showcase product."}
                </p>

                {/* Price */}
                <div className="mt-3 flex items-baseline gap-1">
                  <span className="text-xl font-black text-slate-900 dark:text-white">
                    ${product.price.toFixed(2)}
                  </span>
                  <span className="text-xs font-semibold uppercase text-slate-400">
                    {product.currency || "USD"}
                  </span>
                </div>
              </div>

              {/* Stock Status & Actions */}
              <div className="mt-5 space-y-3 pt-3 border-t border-slate-100 dark:border-slate-800/60">
                {/* Stock Indicator Badge */}
                <div className="flex items-center justify-between text-xs">
                  <span className="text-slate-500 dark:text-slate-400">Live Inventory:</span>
                  {isCheckingStock ? (
                    <span className="inline-flex items-center gap-1 text-slate-400">
                      <Loader2 className="h-3 w-3 animate-spin text-indigo-500" />
                      <span>Checking...</span>
                    </span>
                  ) : isOutOfStock ? (
                    <span className="inline-flex items-center gap-1 rounded-full bg-rose-50 px-2.5 py-0.5 font-semibold text-rose-700 border border-rose-200 dark:bg-rose-950/80 dark:text-rose-300 dark:border-rose-900">
                      <XCircle className="h-3 w-3" />
                      Out of Stock
                    </span>
                  ) : (
                    <span className="inline-flex items-center gap-1 rounded-full bg-emerald-50 px-2.5 py-0.5 font-semibold text-emerald-700 border border-emerald-200 dark:bg-emerald-950/80 dark:text-emerald-300 dark:border-emerald-900">
                      <CheckCircle2 className="h-3 w-3" />
                      In Stock: {availableUnits} units
                    </span>
                  )}
                </div>

                {/* Add to Cart Button */}
                <button
                  onClick={() => handleAddToCart(product, availableUnits)}
                  disabled={isOutOfStock || isCheckingStock}
                  className={`w-full inline-flex items-center justify-center gap-2 rounded-xl py-2.5 px-4 text-xs font-semibold shadow-sm transition duration-150 focus-visible:outline focus-visible:outline-2 ${
                    isOutOfStock
                      ? "bg-slate-100 text-slate-400 border border-slate-200 cursor-not-allowed dark:bg-slate-800 dark:text-slate-500 dark:border-slate-700"
                      : "bg-indigo-600 text-white hover:bg-indigo-500 active:bg-indigo-700 focus-visible:outline-indigo-600 shadow-indigo-600/10"
                  }`}
                >
                  <ShoppingCart className="h-4 w-4" />
                  <span>{isOutOfStock ? "Unavailable" : "Add to Cart"}</span>
                </button>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
};

export default ProductGrid;
