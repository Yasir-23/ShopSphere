"use client";

import React, { createContext, useContext, useState, useEffect, useCallback, useMemo, useRef } from "react";
import { useAuth } from "./AuthContext";
import { getBasket, updateBasket, clearBasket, CustomerBasket, UpsertBasketItem } from "@/lib/basket";

export interface CartItem {
  productId: string;
  productName: string;
  unitPrice: number;
  quantity: number;
  imageUrl?: string;
  category?: string;
  totalPrice?: number;
}

interface CartContextType {
  items: CartItem[];
  totalItems: number;
  subtotal: number;
  isCartOpen: boolean;
  syncing: boolean;
  openCart: () => void;
  closeCart: () => void;
  toggleCart: () => void;
  addToCart: (product: { id: string; name: string; price: number; imageUrl?: string; category?: string }, quantity?: number) => Promise<void>;
  updateQuantity: (productId: string, quantity: number) => Promise<void>;
  removeFromCart: (productId: string) => Promise<void>;
  clearCart: () => Promise<void>;
  refreshCart: () => Promise<void>;
}

const LOCAL_STORAGE_KEY = "shopsphere_local_cart";

const CartContext = createContext<CartContextType | undefined>(undefined);

export const CartProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const { user, loading: authLoading } = useAuth();
  const [items, setItems] = useState<CartItem[]>([]);
  const [isCartOpen, setIsCartOpen] = useState<boolean>(false);
  const [syncing, setSyncing] = useState<boolean>(false);
  const [hasInitialized, setHasInitialized] = useState<boolean>(false);

  // Keep ref to latest items for safe callbacks without circular dependencies
  const itemsRef = useRef<CartItem[]>(items);
  useEffect(() => {
    itemsRef.current = items;
  }, [items]);

  // Calculate totals
  const totalItems = useMemo(() => {
    return items.reduce((acc, item) => acc + item.quantity, 0);
  }, [items]);

  const subtotal = useMemo(() => {
    return items.reduce((acc, item) => acc + item.unitPrice * item.quantity, 0);
  }, [items]);

  // Load from localStorage on mount
  useEffect(() => {
    try {
      const stored = localStorage.getItem(LOCAL_STORAGE_KEY);
      if (stored) {
        const parsed = JSON.parse(stored);
        if (Array.isArray(parsed)) {
          setItems(parsed);
        }
      }
    } catch (e) {
      console.error("Failed to load local cart:", e);
    } finally {
      setHasInitialized(true);
    }
  }, []);

  // Save to localStorage whenever items change
  useEffect(() => {
    if (!hasInitialized) return;
    try {
      localStorage.setItem(LOCAL_STORAGE_KEY, JSON.stringify(items));
    } catch (e) {
      console.error("Failed to persist local cart:", e);
    }
  }, [items, hasInitialized]);

  // Sync to Redis backend helper (guarded by auth status)
  const syncToBackend = useCallback(async (currentItems: CartItem[]) => {
    if (authLoading || !user?.token) {
      // Unauthenticated or hydrating: keep state purely local
      return;
    }

    setSyncing(true);
    try {
      if (currentItems.length === 0) {
        await clearBasket();
      } else {
        const upsertPayload: UpsertBasketItem[] = currentItems.map((i) => ({
          productId: i.productId,
          productName: i.productName,
          unitPrice: i.unitPrice,
          quantity: i.quantity,
        }));
        const updated = await updateBasket(upsertPayload);
        if (updated && Array.isArray(updated.items)) {
          // Keep rich local metadata (imageUrl, category) while updating canonical prices
          setItems((prev) =>
            updated.items.map((bItem) => {
              const local = prev.find((p) => p.productId === bItem.productId);
              return {
                productId: bItem.productId,
                productName: bItem.productName,
                unitPrice: bItem.unitPrice,
                quantity: bItem.quantity,
                totalPrice: bItem.totalPrice,
                imageUrl: local?.imageUrl,
                category: local?.category,
              };
            })
          );
        }
      }
    } catch (err) {
      console.error("Error syncing cart to Redis backend:", err);
    } finally {
      setSyncing(false);
    }
  }, [authLoading, user?.token]);

  // Sync with Redis backend when user becomes authenticated or refreshes
  const refreshCart = useCallback(async () => {
    if (authLoading || !user?.token) {
      return;
    }

    setSyncing(true);
    try {
      const remote = await getBasket();
      if (remote && Array.isArray(remote.items) && remote.items.length > 0) {
        setItems((prev) => {
          // Merge local and remote items
          const mergedMap = new Map<string, CartItem>();

          remote.items.forEach((r) => {
            mergedMap.set(r.productId, {
              productId: r.productId,
              productName: r.productName,
              unitPrice: r.unitPrice,
              quantity: r.quantity,
              totalPrice: r.totalPrice,
            });
          });

          // Retain local image / category metadata
          prev.forEach((local) => {
            const existing = mergedMap.get(local.productId);
            if (existing) {
              existing.imageUrl = local.imageUrl;
              existing.category = local.category;
            } else {
              mergedMap.set(local.productId, local);
            }
          });

          return Array.from(mergedMap.values());
        });
      } else if (itemsRef.current.length > 0) {
        // Push initial local cart to Redis
        await syncToBackend(itemsRef.current);
      }
    } catch (err) {
      console.error("Error refreshing basket:", err);
    } finally {
      setSyncing(false);
    }
  }, [authLoading, user?.token, syncToBackend]);

  // Prevent race condition: wait for auth initialization before triggering refreshCart
  useEffect(() => {
    if (authLoading || !hasInitialized) return;

    if (user?.token) {
      refreshCart();
    }
  }, [authLoading, user?.token, hasInitialized]); // eslint-disable-line react-hooks/exhaustive-deps

  const addToCart = useCallback(
    async (
      product: { id: string; name: string; price: number; imageUrl?: string; category?: string },
      quantity = 1
    ) => {
      let updatedItems: CartItem[] = [];
      setItems((prev) => {
        const existingIdx = prev.findIndex((i) => i.productId === product.id);
        if (existingIdx > -1) {
          const next = [...prev];
          const curr = next[existingIdx];
          const newQty = curr.quantity + quantity;
          next[existingIdx] = {
            ...curr,
            quantity: newQty,
            totalPrice: newQty * curr.unitPrice,
          };
          updatedItems = next;
          return next;
        } else {
          const newItem: CartItem = {
            productId: product.id,
            productName: product.name,
            unitPrice: product.price,
            quantity,
            totalPrice: quantity * product.price,
            imageUrl: product.imageUrl,
            category: product.category,
          };
          updatedItems = [...prev, newItem];
          return updatedItems;
        }
      });

      if (!authLoading && user?.token) {
        await syncToBackend(updatedItems);
      }
    },
    [authLoading, user?.token, syncToBackend]
  );

  const updateQuantity = useCallback(
    async (productId: string, quantity: number) => {
      let updatedItems: CartItem[] = [];
      setItems((prev) => {
        if (quantity <= 0) {
          updatedItems = prev.filter((i) => i.productId !== productId);
          return updatedItems;
        }
        updatedItems = prev.map((item) => {
          if (item.productId === productId) {
            return {
              ...item,
              quantity,
              totalPrice: quantity * item.unitPrice,
            };
          }
          return item;
        });
        return updatedItems;
      });

      if (!authLoading && user?.token) {
        await syncToBackend(updatedItems);
      }
    },
    [authLoading, user?.token, syncToBackend]
  );

  const removeFromCart = useCallback(
    async (productId: string) => {
      let updatedItems: CartItem[] = [];
      setItems((prev) => {
        updatedItems = prev.filter((i) => i.productId !== productId);
        return updatedItems;
      });

      if (!authLoading && user?.token) {
        await syncToBackend(updatedItems);
      }
    },
    [authLoading, user?.token, syncToBackend]
  );

  const clearCart = useCallback(async () => {
    setItems([]);
    try {
      localStorage.removeItem(LOCAL_STORAGE_KEY);
    } catch {}

    if (!authLoading && user?.token) {
      setSyncing(true);
      try {
        await clearBasket();
      } catch (err) {
        console.error("Failed to clear remote basket:", err);
      } finally {
        setSyncing(false);
      }
    }
  }, [authLoading, user?.token]);

  const openCart = useCallback(() => setIsCartOpen(true), []);
  const closeCart = useCallback(() => setIsCartOpen(false), []);
  const toggleCart = useCallback(() => setIsCartOpen((prev) => !prev), []);

  return (
    <CartContext.Provider
      value={{
        items,
        totalItems,
        subtotal,
        isCartOpen,
        syncing,
        openCart,
        closeCart,
        toggleCart,
        addToCart,
        updateQuantity,
        removeFromCart,
        clearCart,
        refreshCart,
      }}
    >
      {children}
    </CartContext.Provider>
  );
};

export const useCart = (): CartContextType => {
  const context = useContext(CartContext);
  if (!context) {
    throw new Error("useCart must be used within a CartProvider");
  }
  return context;
};
