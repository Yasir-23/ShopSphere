import axios from "axios";
import { api } from "./api";

export interface Product {
  id: string;
  name: string;
  slug: string;
  description: string;
  sku: string;
  price: number;
  currency: string;
  category: string;
  categoryId?: string | null;
  attributes?: Record<string, string>;
  images?: string[];
  imageUrl?: string | null;
  isAvailable: boolean;
  isActive: boolean;
  createdAtUtc?: string;
  updatedAtUtc?: string;
}

export interface Category {
  id: string;
  name: string;
  description?: string;
}

export interface StockLevel {
  productId: string;
  sku: string;
  quantityOnHand: number;
  quantityReserved: number;
  quantityAvailable: number;
  restockThreshold: number;
}

/** `null` means the product has no inventory record. */
export type StockMap = Record<string, StockLevel | null>;

export interface NewProduct {
  name: string;
  description: string;
  sku: string;
  price: number;
  category: string;
  categoryId?: string;
  imageUrl?: string;
  isAvailable: boolean;
}

interface Paged<T> {
  items: T[];
  totalCount: number;
}

export function productImage(p: Pick<Product, "imageUrl" | "images">): string | undefined {
  return p.imageUrl || p.images?.[0] || undefined;
}

export async function getProducts(): Promise<Product[]> {
  const { data } = await api.get<Paged<Product>>("/catalog/products", {
    params: { pageNumber: 1, pageSize: 100, sortBy: "newest" },
  });
  return data.items ?? [];
}

export async function getCategories(): Promise<Category[]> {
  const { data } = await api.get<Category[]>("/catalog/categories");
  return Array.isArray(data) ? data : [];
}

export async function getStock(productId: string): Promise<StockLevel | null> {
  try {
    const { data } = await api.get<StockLevel>(`/inventory/${encodeURIComponent(productId)}`);
    return data;
  } catch (error) {
    if (axios.isAxiosError(error) && error.response?.status === 404) return null;
    throw error;
  }
}

/** Fetch stock for many products; individual failures resolve to `null`. */
export async function getStockMap(productIds: string[]): Promise<StockMap> {
  const entries = await Promise.all(
    productIds.map(async (id) => [id, await getStock(id).catch(() => null)] as const)
  );
  return Object.fromEntries(entries);
}

function productBody(input: NewProduct) {
  return {
    ...input,
    currency: "USD",
    imageUrl: input.imageUrl || null,
    images: input.imageUrl ? [input.imageUrl] : [],
  };
}

export async function createProduct(input: NewProduct): Promise<Product> {
  const { data } = await api.post<Product>("/catalog/products", productBody(input));
  return data;
}

export async function updateProduct(id: string, input: NewProduct): Promise<Product> {
  const { data } = await api.put<Product>(`/catalog/products/${encodeURIComponent(id)}`, productBody(input));
  return data;
}

export async function deleteProduct(id: string): Promise<void> {
  await api.delete(`/catalog/products/${encodeURIComponent(id)}`);
}

/** Create the stock record for a product (Inventory service, PostgreSQL). */
export async function provisionStock(productId: string, sku: string, quantity: number): Promise<StockLevel> {
  const { data } = await api.post<StockLevel>("/inventory/items", { productId, sku, quantity });
  return data;
}

/** Add (positive) or remove (negative) units. Removal can't go below units reserved for open orders. */
export async function adjustStock(productId: string, quantity: number): Promise<StockLevel> {
  const { data } = await api.post<StockLevel>("/inventory/adjust", {
    productId,
    quantity,
    reason: quantity > 0 ? "Restocked from admin console" : "Removed from admin console",
  });
  return data;
}

/** Remove a product's stock record. Resolves quietly if none exists. */
export async function deleteStock(productId: string): Promise<void> {
  try {
    await api.delete(`/inventory/items/${encodeURIComponent(productId)}`);
  } catch (error) {
    if (axios.isAxiosError(error) && error.response?.status === 404) return;
    throw error;
  }
}
