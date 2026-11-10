"use client";

import React, {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  useSyncExternalStore,
} from "react";
import { useAuth } from "./AuthContext";
import { deleteBasket, fetchBasket, saveBasket, type Basket } from "@/lib/basket";
import { getErrorMessage } from "@/lib/api";

export interface CartItem {
  productId: string;
  productName: string;
  unitPrice: number;
  quantity: number;
  imageUrl?: string;
  category?: string;
  /** Units available when the item was added; caps the quantity stepper. */
  maxQuantity?: number;
}

export interface AddableProduct {
  id: string;
  name: string;
  price: number;
  imageUrl?: string;
  category?: string;
}

interface CartContextValue extends CartActions {
  items: CartItem[];
  totalItems: number;
  subtotal: number;
}

interface CartActions {
  isOpen: boolean;
  syncError: string | null;
  openCart: () => void;
  closeCart: () => void;
  addItem: (product: AddableProduct, maxQuantity?: number) => void;
  setQuantity: (productId: string, quantity: number) => void;
  removeItem: (productId: string) => void;
  clearCart: () => Promise<void>;
}

// ---------------------------------------------------------------------------
// localStorage-backed cart store
// ---------------------------------------------------------------------------
const STORAGE_KEY = "shopsphere_cart";
const CHANGE_EVENT = "shopsphere:cart";
const EMPTY: CartItem[] = [];
let cachedRaw: string | null | undefined;
let cachedItems: CartItem[] = EMPTY;

function readCart(): CartItem[] {
  let raw: string | null = null;
  try {
    raw = localStorage.getItem(STORAGE_KEY);
  } catch {
    /* storage unavailable */
  }
  if (raw === cachedRaw) return cachedItems;
  cachedRaw = raw;
  try {
    const parsed = raw ? JSON.parse(raw) : [];
    cachedItems = Array.isArray(parsed) ? (parsed as CartItem[]) : EMPTY;
  } catch {
    cachedItems = EMPTY;
  }
  return cachedItems;
}

function writeCart(items: CartItem[]): void {
  try {
    if (items.length) localStorage.setItem(STORAGE_KEY, JSON.stringify(items));
    else localStorage.removeItem(STORAGE_KEY);
  } catch {
    /* storage unavailable */
  }
  window.dispatchEvent(new Event(CHANGE_EVENT));
}

function subscribeCart(cb: () => void): () => void {
  const onStorage = (e: StorageEvent) => e.key === STORAGE_KEY && cb();
  window.addEventListener(CHANGE_EVENT, cb);
  window.addEventListener("storage", onStorage);
  return () => {
    window.removeEventListener(CHANGE_EVENT, cb);
    window.removeEventListener("storage", onStorage);
  };
}

/** Overlay server-verified prices/names on local items, keeping display metadata. */
function applyServerBasket(local: CartItem[], basket: Basket): CartItem[] {
  return basket.items.map((line) => {
    const existing = local.find((i) => i.productId === line.productId);
    return {
      ...existing,
      productId: line.productId,
      productName: line.productName,
      unitPrice: line.unitPrice,
      quantity: line.quantity,
    };
  });
}

const toLines = (items: CartItem[]) =>
  items.map(({ productId, productName, unitPrice, quantity }) => ({ productId, productName, unitPrice, quantity }));

// ---------------------------------------------------------------------------

// Like auth, cart items are read per-consumer in useCart() (not passed through
// context) so each component hydrates against the empty server snapshot.
const CartContext = createContext<CartActions | undefined>(undefined);

export function CartProvider({ children }: { children: React.ReactNode }) {
  const { user, ready } = useAuth();
  const [isOpen, setIsOpen] = useState(false);
  const [syncError, setSyncError] = useState<string | null>(null);

  const userId = user?.id ?? null;
  const syncTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  const previousUser = useRef<string | null | undefined>(undefined);

  /** Push the current cart to the Redis basket (debounced). */
  const scheduleSync = useCallback(() => {
    if (!userId) return;
    if (syncTimer.current) clearTimeout(syncTimer.current);
    syncTimer.current = setTimeout(async () => {
      syncTimer.current = null;
      const current = readCart();
      try {
        if (current.length === 0) {
          await deleteBasket();
        } else {
          const basket = await saveBasket(toLines(current));
          // Only adopt server prices if the cart wasn't edited meanwhile.
          if (readCart() === current) writeCart(applyServerBasket(current, basket));
        }
        setSyncError(null);
      } catch (error) {
        setSyncError(getErrorMessage(error, "We couldn't save your cart. Your items are kept on this device."));
      }
    }, 350);
  }, [userId]);

  // On sign-in: merge the saved basket with anything added as a guest.
  // On sign-out: drop the device copy (the basket stays saved to the account).
  useEffect(() => {
    if (!ready) return;
    const prev = previousUser.current;
    previousUser.current = userId;
    if (prev === userId) return;

    if (!userId) {
      if (prev) writeCart([]);
      return;
    }

    let cancelled = false;
    (async () => {
      try {
        const remote = await fetchBasket();
        if (cancelled) return;
        const local = readCart();
        const merged = new Map<string, CartItem>();
        for (const line of remote.items) merged.set(line.productId, { ...line });
        for (const item of local) {
          const existing = merged.get(item.productId);
          merged.set(item.productId, existing ? { ...item, quantity: Math.max(existing.quantity, item.quantity) } : item);
        }
        const mergedItems = Array.from(merged.values());
        writeCart(mergedItems);
        const differs =
          mergedItems.length !== remote.items.length ||
          mergedItems.some((m) => remote.items.find((r) => r.productId === m.productId)?.quantity !== m.quantity);
        if (differs) scheduleSync();
        setSyncError(null);
      } catch (error) {
        if (!cancelled) setSyncError(getErrorMessage(error, "We couldn't load your saved cart."));
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [ready, userId, scheduleSync]);

  useEffect(
    () => () => {
      if (syncTimer.current) clearTimeout(syncTimer.current);
    },
    []
  );

  const update = useCallback(
    (next: CartItem[]) => {
      writeCart(next);
      scheduleSync();
    },
    [scheduleSync]
  );

  const addItem = useCallback(
    (product: AddableProduct, maxQuantity?: number) => {
      const current = readCart();
      const existing = current.find((i) => i.productId === product.id);
      const cap = maxQuantity ?? existing?.maxQuantity;
      if (existing) {
        const quantity = cap !== undefined ? Math.min(existing.quantity + 1, cap) : existing.quantity + 1;
        update(current.map((i) => (i.productId === product.id ? { ...i, quantity, maxQuantity: cap } : i)));
      } else {
        update([
          ...current,
          {
            productId: product.id,
            productName: product.name,
            unitPrice: product.price,
            quantity: 1,
            imageUrl: product.imageUrl,
            category: product.category,
            maxQuantity: cap,
          },
        ]);
      }
      setIsOpen(true);
    },
    [update]
  );

  const setQuantity = useCallback(
    (productId: string, quantity: number) => {
      const current = readCart();
      if (quantity <= 0) {
        update(current.filter((i) => i.productId !== productId));
        return;
      }
      update(
        current.map((i) =>
          i.productId === productId
            ? { ...i, quantity: i.maxQuantity !== undefined ? Math.min(quantity, i.maxQuantity) : quantity }
            : i
        )
      );
    },
    [update]
  );

  const removeItem = useCallback(
    (productId: string) => update(readCart().filter((i) => i.productId !== productId)),
    [update]
  );

  const clearCart = useCallback(async () => {
    if (syncTimer.current) {
      clearTimeout(syncTimer.current);
      syncTimer.current = null;
    }
    writeCart([]);
    if (userId) await deleteBasket().catch(() => undefined);
  }, [userId]);

  const openCart = useCallback(() => setIsOpen(true), []);
  const closeCart = useCallback(() => setIsOpen(false), []);

  const actions = useMemo<CartActions>(
    () => ({ isOpen, syncError, openCart, closeCart, addItem, setQuantity, removeItem, clearCart }),
    [isOpen, syncError, openCart, closeCart, addItem, setQuantity, removeItem, clearCart]
  );

  return <CartContext.Provider value={actions}>{children}</CartContext.Provider>;
}

export function useCart(): CartContextValue {
  const actions = useContext(CartContext);
  if (!actions) throw new Error("useCart must be used within a CartProvider");
  const items = useSyncExternalStore(subscribeCart, readCart, () => EMPTY);
  const totals = useMemo(
    () => ({
      totalItems: items.reduce((n, i) => n + i.quantity, 0),
      subtotal: items.reduce((n, i) => n + i.unitPrice * i.quantity, 0),
    }),
    [items]
  );
  return { ...actions, items, ...totals };
}
