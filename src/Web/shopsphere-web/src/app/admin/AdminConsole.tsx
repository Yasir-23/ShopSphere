"use client";

import { useEffect, useState } from "react";
import { Boxes, CircleCheck, Loader2, Minus, PackagePlus, Pencil, Plus, RefreshCw, Trash2, TriangleAlert, X } from "lucide-react";
import { getErrorMessage } from "@/lib/api";
import {
  adjustStock,
  createProduct,
  deleteProduct,
  deleteStock,
  getProducts,
  getStockMap,
  productImage,
  provisionStock,
  updateProduct,
  type Category,
  type NewProduct,
  type Product,
  type StockLevel,
  type StockMap,
} from "@/lib/catalog";
import { loadCategories } from "@/lib/useCategories";
import { formatPrice } from "@/lib/format";
import { ProductImage } from "@/components/ProductImage";
import { cn } from "@/lib/utils";

type Data = { products: Product[]; stock: StockMap; categories: Category[] };
type State = { status: "loading" } | { status: "error"; message: string } | ({ status: "ready" } & Data);
type Notice = { tone: "success" | "error"; text: string };

async function loadAll(): Promise<Data> {
  const [products, categories] = await Promise.all([getProducts(), loadCategories()]);
  const stock = await getStockMap(products.map((p) => p.id));
  return { products, stock, categories };
}

function StatTile({ label, value, tone = "default" }: { label: string; value: number | string; tone?: "default" | "warn" }) {
  return (
    <div className="rounded-2xl border border-slate-200 bg-white p-5">
      <p className="text-sm text-slate-500">{label}</p>
      <p className={cn("mt-1 text-3xl font-bold tabular-nums", tone === "warn" ? "text-amber-600" : "text-slate-900")}>{value}</p>
    </div>
  );
}

const inputClass = (invalid?: string) =>
  cn(
    "mt-1 w-full rounded-lg border px-3 py-2 text-sm focus:outline-none focus:ring-2",
    invalid ? "border-rose-300 focus:ring-rose-100" : "border-slate-300 focus:border-indigo-400 focus:ring-indigo-100"
  );

// ---------------------------------------------------------------------------
// Product form (shared by "Add a product" and the edit dialog)
// ---------------------------------------------------------------------------
interface FormValues {
  name: string;
  description: string;
  sku: string;
  price: string;
  category: string;
  imageUrl: string;
  isAvailable: boolean;
  initialStock: string;
}

const EMPTY_FORM: FormValues = {
  name: "",
  description: "",
  sku: "",
  price: "",
  category: "",
  imageUrl: "",
  isAvailable: true,
  initialStock: "50",
};

function toFormValues(p: Product): FormValues {
  return {
    name: p.name,
    description: p.description,
    sku: p.sku,
    price: String(p.price),
    category: p.category,
    imageUrl: productImage(p) ?? "",
    isAvailable: p.isAvailable,
    initialStock: "",
  };
}

function validate(form: FormValues, withStock: boolean) {
  const price = Number(form.price);
  const stock = Number(form.initialStock);
  return {
    name: !form.name.trim() ? "Name is required." : form.name.length > 200 ? "Keep the name under 200 characters." : undefined,
    description: form.description.trim() ? undefined : "Description is required.",
    sku: !form.sku.trim() ? "SKU is required." : form.sku.length > 50 ? "Keep the SKU under 50 characters." : undefined,
    price: form.price && Number.isFinite(price) && price > 0 ? undefined : "Enter a price greater than zero.",
    category: form.category ? undefined : "Choose a category.",
    imageUrl: !form.imageUrl || /^https?:\/\/\S+$/i.test(form.imageUrl.trim()) ? undefined : "Enter a full http(s) URL.",
    initialStock:
      !withStock || (form.initialStock !== "" && Number.isInteger(stock) && stock >= 0 && stock <= 1_000_000)
        ? undefined
        : "Enter a whole number of units (0 or more).",
  };
}

function toProductInput(form: FormValues, categories: Category[]): NewProduct {
  return {
    name: form.name.trim(),
    description: form.description.trim(),
    sku: form.sku.trim().toUpperCase(),
    price: Math.round(Number(form.price) * 100) / 100,
    category: form.category,
    categoryId: categories.find((c) => c.name === form.category)?.id,
    imageUrl: form.imageUrl.trim() || undefined,
    isAvailable: form.isAvailable,
  };
}

function ProductForm({
  categories,
  initial,
  withStock,
  submitLabel,
  savingLabel,
  onSubmit,
  onCancel,
}: {
  categories: Category[];
  initial: FormValues;
  withStock: boolean;
  submitLabel: string;
  savingLabel: string;
  onSubmit: (values: FormValues) => Promise<void>;
  onCancel?: () => void;
}) {
  const [form, setForm] = useState(initial);
  const [touched, setTouched] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const errors = validate(form, withStock);
  const show = (k: keyof typeof errors) => (touched ? errors[k] : undefined);
  const set = (k: keyof FormValues) => (e: React.ChangeEvent<HTMLInputElement | HTMLTextAreaElement | HTMLSelectElement>) =>
    setForm((f) => ({ ...f, [k]: e.target.value }));
  const fieldError = (k: keyof typeof errors) => show(k) && <span className="mt-1 block text-xs text-rose-600">{show(k)}</span>;

  async function submit(e: React.FormEvent) {
    e.preventDefault();
    setTouched(true);
    setError(null);
    if (Object.values(errors).some(Boolean)) return;
    setSaving(true);
    try {
      await onSubmit(form);
      if (withStock) {
        setForm(EMPTY_FORM);
        setTouched(false);
      }
    } catch (err) {
      setError(getErrorMessage(err, "The product couldn't be saved."));
    } finally {
      setSaving(false);
    }
  }

  // Keep the edited category selectable even if it isn't in the category list.
  const categoryNames = Array.from(new Set([...categories.map((c) => c.name), ...(form.category ? [form.category] : [])]));

  return (
    <form onSubmit={submit} noValidate className="space-y-3">
      {error && (
        <p role="alert" className="rounded-lg bg-rose-50 px-3 py-2 text-sm text-rose-700">
          {error}
        </p>
      )}
      <label className="block text-sm">
        <span className="font-medium text-slate-700">Product name</span>
        <input name="name" value={form.name} onChange={set("name")} className={inputClass(show("name"))} placeholder="e.g. USB-C Charging Stand" />
        {fieldError("name")}
      </label>
      <label className="block text-sm">
        <span className="font-medium text-slate-700">Description</span>
        <textarea name="description" rows={3} value={form.description} onChange={set("description")} className={inputClass(show("description"))} />
        {fieldError("description")}
      </label>
      <div className="grid grid-cols-2 gap-3">
        <label className="block text-sm">
          <span className="font-medium text-slate-700">SKU</span>
          <input name="sku" value={form.sku} onChange={set("sku")} className={cn(inputClass(show("sku")), "uppercase")} placeholder="ACC-USBC-001" />
          {fieldError("sku")}
        </label>
        <label className="block text-sm">
          <span className="font-medium text-slate-700">Price (USD)</span>
          <input name="price" type="number" min="0.01" step="0.01" inputMode="decimal" value={form.price} onChange={set("price")} className={inputClass(show("price"))} placeholder="49.99" />
          {fieldError("price")}
        </label>
      </div>
      <div className={cn("grid gap-3", withStock && "grid-cols-2")}>
        <label className="block text-sm">
          <span className="font-medium text-slate-700">Category</span>
          <select name="category" value={form.category} onChange={set("category")} className={cn(inputClass(show("category")), "bg-white")}>
            <option value="">Select a category</option>
            {categoryNames.map((c) => (
              <option key={c} value={c}>
                {c}
              </option>
            ))}
          </select>
          {fieldError("category")}
        </label>
        {withStock && (
          <label className="block text-sm">
            <span className="font-medium text-slate-700">Initial stock</span>
            <input name="initialStock" type="number" min="0" step="1" inputMode="numeric" value={form.initialStock} onChange={set("initialStock")} className={inputClass(show("initialStock"))} />
            {fieldError("initialStock")}
          </label>
        )}
      </div>
      <label className="block text-sm">
        <span className="font-medium text-slate-700">
          Image URL <span className="font-normal text-slate-400">(optional)</span>
        </span>
        <input name="imageUrl" type="url" value={form.imageUrl} onChange={set("imageUrl")} className={inputClass(show("imageUrl"))} placeholder="https://…" />
        {fieldError("imageUrl")}
      </label>
      <label className="flex items-center gap-2 text-sm text-slate-700">
        <input
          type="checkbox"
          name="isAvailable"
          checked={form.isAvailable}
          onChange={(e) => setForm((f) => ({ ...f, isAvailable: e.target.checked }))}
          className="h-4 w-4 accent-indigo-600"
        />
        Available for sale
      </label>
      <div className="flex gap-2">
        {onCancel && (
          <button
            type="button"
            onClick={onCancel}
            className="flex-1 rounded-lg border border-slate-300 py-2.5 text-sm font-semibold text-slate-700 hover:bg-slate-50"
          >
            Cancel
          </button>
        )}
        <button
          type="submit"
          disabled={saving}
          className="flex flex-1 items-center justify-center gap-2 rounded-lg bg-indigo-600 py-2.5 text-sm font-semibold text-white hover:bg-indigo-500 disabled:opacity-60"
        >
          {saving ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> : withStock ? <Plus className="h-4 w-4" aria-hidden /> : null}
          {saving ? savingLabel : submitLabel}
        </button>
      </div>
    </form>
  );
}

function EditProductDialog({
  product,
  categories,
  onSaved,
  onClose,
}: {
  product: Product;
  categories: Category[];
  onSaved: (p: Product) => void;
  onClose: () => void;
}) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === "Escape" && onClose();
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [onClose]);

  return (
    <div className="fixed inset-0 z-[60] flex items-center justify-center bg-slate-900/50 p-4">
      <div role="dialog" aria-modal="true" aria-labelledby="edit-product-title" className="max-h-[95vh] w-full max-w-lg overflow-y-auto rounded-2xl bg-white p-6 shadow-2xl">
        <div className="mb-4 flex items-center justify-between">
          <h2 id="edit-product-title" className="text-lg font-semibold text-slate-900">
            Edit product
          </h2>
          <button type="button" onClick={onClose} className="rounded-full p-2 text-slate-500 hover:bg-slate-100" aria-label="Close">
            <X className="h-5 w-5" />
          </button>
        </div>
        <ProductForm
          categories={categories}
          initial={toFormValues(product)}
          withStock={false}
          submitLabel="Save changes"
          savingLabel="Saving…"
          onCancel={onClose}
          onSubmit={async (values) => onSaved(await updateProduct(product.id, toProductInput(values, categories)))}
        />
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Product row
// ---------------------------------------------------------------------------
function StockControls({
  product,
  stock,
  onStock,
  onError,
}: {
  product: Product;
  stock: StockLevel | null;
  onStock: (s: StockLevel, message: string) => void;
  onError: (message: string) => void;
}) {
  const [qty, setQty] = useState(stock ? "10" : "50");
  const [busy, setBusy] = useState<"add" | "remove" | "setup" | null>(null);

  async function run(kind: "add" | "remove" | "setup") {
    const n = Number(qty);
    if (!Number.isInteger(n) || n < (kind === "setup" ? 0 : 1)) {
      onError("Enter a whole number of units.");
      return;
    }
    setBusy(kind);
    try {
      if (kind === "setup") {
        onStock(await provisionStock(product.id, product.sku, n), `Stock set up for “${product.name}” with ${n} units.`);
      } else {
        const delta = kind === "add" ? n : -n;
        onStock(await adjustStock(product.id, delta), `${kind === "add" ? "Added" : "Removed"} ${n} units ${kind === "add" ? "to" : "from"} “${product.name}”.`);
      }
    } catch (err) {
      onError(getErrorMessage(err, "Stock couldn't be updated."));
    } finally {
      setBusy(null);
    }
  }

  const button = (kind: "add" | "remove" | "setup", label: string, Icon: typeof Plus) => (
    <button
      type="button"
      onClick={() => run(kind)}
      disabled={busy !== null}
      className="inline-flex items-center gap-1 border-l border-slate-200 px-2 py-1.5 text-xs font-semibold text-indigo-700 hover:bg-indigo-50 disabled:opacity-50"
      aria-label={`${label} for ${product.name}`}
    >
      {busy === kind ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Icon className="h-3.5 w-3.5" />}
      {label}
    </button>
  );

  return (
    <div className="flex items-center rounded-lg border border-slate-200">
      <input
        type="number"
        min={stock ? 1 : 0}
        value={qty}
        onChange={(e) => setQty(e.target.value)}
        className="w-14 rounded-l-lg px-2 py-1.5 text-sm focus:outline-none"
        aria-label={`Units for ${product.name}`}
      />
      {stock ? (
        <>
          {button("add", "Add", Plus)}
          {button("remove", "Remove", Minus)}
        </>
      ) : (
        button("setup", "Set up stock", PackagePlus)
      )}
    </div>
  );
}

function ProductRow({
  product,
  stock,
  onStock,
  onEdit,
  onDeleted,
  onError,
}: {
  product: Product;
  stock: StockMap[string] | undefined;
  onStock: (s: StockLevel, message: string) => void;
  onEdit: () => void;
  onDeleted: () => void;
  onError: (message: string) => void;
}) {
  const [deleting, setDeleting] = useState(false);
  const low = stock && stock.quantityAvailable <= stock.restockThreshold;

  async function remove() {
    if (!window.confirm(`Remove “${product.name}” from the catalog? This can't be undone.`)) return;
    setDeleting(true);
    try {
      // Stock first: it refuses while units are reserved for open orders.
      await deleteStock(product.id);
      await deleteProduct(product.id);
      onDeleted();
    } catch (err) {
      onError(getErrorMessage(err, "The product couldn't be removed."));
      setDeleting(false);
    }
  }

  return (
    <tr className="align-middle">
      <td className="py-3 pl-4 pr-3">
        <div className="flex items-center gap-3">
          <ProductImage src={productImage(product)} alt={product.name} category={product.category} className="h-11 w-11 shrink-0 rounded-lg" sizes="44px" />
          <div className="min-w-0">
            <p className="max-w-56 truncate font-medium text-slate-900" title={product.name}>
              {product.name}
            </p>
            <p className="font-mono text-xs text-slate-500">{product.sku}</p>
          </div>
        </div>
      </td>
      <td className="px-3 py-3 text-slate-600">{product.category}</td>
      <td className="px-3 py-3 text-right font-medium tabular-nums text-slate-900">{formatPrice(product.price)}</td>
      <td className="px-3 py-3">
        {stock === undefined ? (
          <span className="text-slate-400">…</span>
        ) : stock === null ? (
          <span className="text-xs font-medium text-amber-700">No stock set up</span>
        ) : (
          <div>
            <p className={cn("font-medium tabular-nums", stock.quantityAvailable <= 0 ? "text-rose-600" : low ? "text-amber-600" : "text-slate-900")}>
              {stock.quantityAvailable} available
            </p>
            <p className="text-xs text-slate-500">
              {stock.quantityOnHand} on hand{stock.quantityReserved ? ` · ${stock.quantityReserved} reserved` : ""}
              {low ? " · low" : ""}
            </p>
          </div>
        )}
      </td>
      <td className="px-3 py-3">
        <span className={cn("rounded-full px-2 py-0.5 text-xs font-semibold", product.isAvailable ? "bg-emerald-50 text-emerald-700" : "bg-slate-100 text-slate-600")}>
          {product.isAvailable ? "On sale" : "Hidden"}
        </span>
      </td>
      <td className="py-3 pl-3 pr-4">
        <div className="flex items-center justify-end gap-1">
          {stock !== undefined && <StockControls key={stock ? "tracked" : "untracked"} product={product} stock={stock} onStock={onStock} onError={onError} />}
          <button
            type="button"
            onClick={onEdit}
            className="rounded-lg p-2 text-slate-400 hover:bg-indigo-50 hover:text-indigo-600"
            aria-label={`Edit ${product.name}`}
            title="Edit product"
          >
            <Pencil className="h-4 w-4" />
          </button>
          <button
            type="button"
            onClick={remove}
            disabled={deleting}
            className="rounded-lg p-2 text-slate-400 hover:bg-rose-50 hover:text-rose-600 disabled:opacity-50"
            aria-label={`Delete ${product.name}`}
            title="Delete product"
          >
            {deleting ? <Loader2 className="h-4 w-4 animate-spin" /> : <Trash2 className="h-4 w-4" />}
          </button>
        </div>
      </td>
    </tr>
  );
}

// ---------------------------------------------------------------------------

export function AdminConsole() {
  const [state, setState] = useState<State>({ status: "loading" });
  const [notice, setNotice] = useState<Notice | null>(null);
  const [editing, setEditing] = useState<Product | null>(null);
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    let active = true;
    loadAll()
      .then((data) => active && setState({ status: "ready", ...data }))
      .catch((error) => active && setState({ status: "error", message: getErrorMessage(error, "Store data couldn't be loaded.") }));
    return () => {
      active = false;
    };
  }, [attempt]);

  const reload = () => {
    setState({ status: "loading" });
    setAttempt((n) => n + 1);
  };

  const patch = (fn: (d: Data) => Data) => setState((s) => (s.status === "ready" ? { status: "ready", ...fn(s) } : s));
  const setStockFor = (productId: string, s: StockLevel | null) => patch((d) => ({ ...d, stock: { ...d.stock, [productId]: s } }));

  const products = state.status === "ready" ? state.products : [];
  const categories = state.status === "ready" ? state.categories : [];
  const stock = state.status === "ready" ? state.stock : {};
  const tracked = Object.values(stock).filter((s): s is StockLevel => !!s);
  const totalUnits = tracked.reduce((n, s) => n + s.quantityAvailable, 0);
  const lowStock = tracked.filter((s) => s.quantityAvailable <= s.restockThreshold).length;

  async function addProduct(values: FormValues) {
    const created = await createProduct(toProductInput(values, categories));
    patch((d) => ({ ...d, products: [created, ...d.products], stock: { ...d.stock, [created.id]: null } }));
    try {
      const level = await provisionStock(created.id, created.sku, Number(values.initialStock));
      setStockFor(created.id, level);
      setNotice({ tone: "success", text: `“${created.name}” was added with ${level.quantityAvailable} units in stock and is now in the store.` });
    } catch (err) {
      setNotice({
        tone: "error",
        text: `“${created.name}” was added, but its stock couldn't be set up (${getErrorMessage(err)}). Use “Set up stock” in the table.`,
      });
    }
  }

  return (
    <div className="mx-auto max-w-7xl px-4 py-10 sm:px-6 lg:px-8">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div>
          <h1 className="text-3xl font-bold tracking-tight text-slate-900">Store management</h1>
          <p className="mt-1 text-sm text-slate-500">Manage the product catalog and stock levels.</p>
        </div>
        <button
          type="button"
          onClick={reload}
          className="inline-flex items-center gap-1.5 rounded-lg border border-slate-300 bg-white px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
        >
          <RefreshCw className="h-4 w-4" aria-hidden /> Refresh
        </button>
      </div>

      {notice && (
        <div
          role="status"
          className={cn(
            "mt-6 flex items-center justify-between gap-3 rounded-xl px-4 py-3 text-sm",
            notice.tone === "success" ? "bg-emerald-50 text-emerald-800" : "bg-rose-50 text-rose-700"
          )}
        >
          <span className="flex items-center gap-2">
            {notice.tone === "success" ? <CircleCheck className="h-4 w-4 shrink-0" /> : <TriangleAlert className="h-4 w-4 shrink-0" />}
            {notice.text}
          </span>
          <button type="button" onClick={() => setNotice(null)} className="text-xs font-semibold hover:underline">
            Dismiss
          </button>
        </div>
      )}

      <div className="mt-6 grid grid-cols-2 gap-4 lg:grid-cols-4">
        <StatTile label="Products" value={state.status === "ready" ? products.length : "–"} />
        <StatTile label="On sale" value={state.status === "ready" ? products.filter((p) => p.isAvailable).length : "–"} />
        <StatTile label="Units available" value={state.status === "ready" ? totalUnits : "–"} />
        <StatTile label="Low stock" value={state.status === "ready" ? lowStock : "–"} tone={lowStock > 0 ? "warn" : "default"} />
      </div>

      <div className="mt-8 grid gap-6 lg:grid-cols-3">
        <section className="overflow-hidden rounded-2xl border border-slate-200 bg-white lg:col-span-2" aria-labelledby="catalog-heading">
          <div className="flex items-center gap-2 border-b border-slate-100 px-4 py-3">
            <Boxes className="h-5 w-5 text-indigo-600" aria-hidden />
            <h2 id="catalog-heading" className="font-semibold text-slate-900">
              Catalog
            </h2>
          </div>
          {state.status === "loading" && (
            <div className="flex justify-center py-16">
              <Loader2 className="h-6 w-6 animate-spin text-indigo-600" />
            </div>
          )}
          {state.status === "error" && (
            <div className="px-6 py-12 text-center">
              <p className="font-semibold text-slate-900">{state.message}</p>
              <button type="button" onClick={reload} className="mt-4 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white">
                Try again
              </button>
            </div>
          )}
          {state.status === "ready" && (
            <div className="overflow-x-auto">
              <table className="w-full min-w-[820px] text-sm">
                <thead className="bg-slate-50 text-left text-xs font-semibold uppercase tracking-wide text-slate-500">
                  <tr>
                    <th className="py-2.5 pl-4 pr-3">Product</th>
                    <th className="px-3 py-2.5">Category</th>
                    <th className="px-3 py-2.5 text-right">Price</th>
                    <th className="px-3 py-2.5">Stock</th>
                    <th className="px-3 py-2.5">Status</th>
                    <th className="py-2.5 pl-3 pr-4 text-right">Actions</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-slate-100">
                  {products.map((p) => (
                    <ProductRow
                      key={p.id}
                      product={p}
                      stock={p.id in stock ? stock[p.id] : undefined}
                      onStock={(s, text) => {
                        setStockFor(p.id, s);
                        setNotice({ tone: "success", text });
                      }}
                      onEdit={() => setEditing(p)}
                      onDeleted={() => {
                        patch((d) => ({ ...d, products: d.products.filter((x) => x.id !== p.id) }));
                        setNotice({ tone: "success", text: `“${p.name}” was removed from the catalog.` });
                      }}
                      onError={(text) => setNotice({ tone: "error", text })}
                    />
                  ))}
                  {products.length === 0 && (
                    <tr>
                      <td colSpan={6} className="px-4 py-12 text-center text-slate-500">
                        No products yet. Add your first product using the form.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          )}
        </section>

        <aside className="lg:sticky lg:top-32 lg:self-start">
          <div className="rounded-2xl border border-slate-200 bg-white p-5" aria-labelledby="add-product-heading">
            <h2 id="add-product-heading" className="mb-3 flex items-center gap-2 text-lg font-semibold text-slate-900">
              <PackagePlus className="h-5 w-5 text-indigo-600" aria-hidden /> Add a product
            </h2>
            <ProductForm
              categories={categories}
              initial={EMPTY_FORM}
              withStock
              submitLabel="Add product"
              savingLabel="Saving…"
              onSubmit={addProduct}
            />
          </div>
          <p className="mt-3 px-1 text-xs text-slate-500">
            New products appear in the store immediately with their initial stock. Customers can only buy products that have stock set up.
          </p>
        </aside>
      </div>

      {editing && (
        <EditProductDialog
          product={editing}
          categories={categories}
          onClose={() => setEditing(null)}
          onSaved={(updated) => {
            patch((d) => ({ ...d, products: d.products.map((x) => (x.id === updated.id ? updated : x)) }));
            setEditing(null);
            setNotice({ tone: "success", text: `“${updated.name}” was updated.` });
          }}
        />
      )}
    </div>
  );
}
