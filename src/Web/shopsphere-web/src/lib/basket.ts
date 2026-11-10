import { api } from "./api";

export interface BasketLine {
  productId: string;
  productName: string;
  unitPrice: number;
  quantity: number;
}

export interface Basket {
  customerId: string;
  items: (BasketLine & { totalPrice: number })[];
  totalAmount: number;
}

/** Read the signed-in customer's basket. The server returns an empty basket if none exists. */
export async function fetchBasket(): Promise<Basket> {
  const { data } = await api.get<Basket>("/basket");
  return data;
}

/**
 * Replace the basket contents. The Basket service re-prices every line
 * against the catalog, so the response carries the authoritative prices.
 */
export async function saveBasket(items: BasketLine[]): Promise<Basket> {
  const { data } = await api.post<Basket>("/basket", { items });
  return data;
}

export async function deleteBasket(): Promise<void> {
  await api.delete("/basket");
}
