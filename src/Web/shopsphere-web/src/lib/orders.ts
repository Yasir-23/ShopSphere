import { api } from "./api";

export interface ShippingAddress {
  street: string;
  city: string;
  state: string;
  zipCode: string;
  country: string;
}

export interface OrderLine {
  id: string;
  productId: string;
  productName: string;
  unitPrice: number;
  quantity: number;
  totalPrice: number;
}

/** Order aggregate as stored by the Ordering service (SQL Server). */
export interface Order {
  id: string;
  customerId: string;
  customerEmail: string;
  shippingAddress: ShippingAddress;
  items: OrderLine[];
  totalAmount: number;
  /** Pending → StockReserved → Completed, or Cancelled. Driven by the checkout saga. */
  status: "Pending" | "StockReserved" | "Paid" | "Completed" | "Cancelled" | string;
  cancellationReason?: string | null;
  createdAtUtc: string;
  updatedAtUtc?: string | null;
}

export interface OrderPage {
  items: Order[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

/** Payment options offered at checkout. The token travels with the order to the Payment service. */
export const PAYMENT_OPTIONS = [
  {
    id: "standard",
    label: "Visa ending 4242",
    hint: "Standard card: payment is approved",
    token: "card_visa_4242",
  },
  {
    id: "decline",
    label: "Test card ending 0000",
    hint: "Test card: the bank declines the payment",
    token: "card_test_0000",
  },
] as const;

export type PaymentOptionId = (typeof PAYMENT_OPTIONS)[number]["id"];

export async function createOrder(
  shippingAddress: ShippingAddress,
  items: { productId: string; productName: string; unitPrice: number; quantity: number }[],
  paymentMethod: string
): Promise<Order> {
  const { data } = await api.post<Order>("/orders", { shippingAddress, items, paymentMethod });
  return data;
}

export async function getOrder(orderId: string): Promise<Order> {
  const { data } = await api.get<Order>(`/orders/${orderId}`);
  return data;
}

export const isSettled = (order: Pick<Order, "status">) =>
  order.status === "Completed" || order.status === "Cancelled";

const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms));

/**
 * Follow the order aggregate until the checkout saga settles it (Completed or
 * Cancelled). Returns the latest order — still unsettled if the timeout elapses.
 */
export async function waitForOrderOutcome(orderId: string, timeoutMs = 30000): Promise<Order> {
  const deadline = Date.now() + timeoutMs;
  let delay = 400;
  let order = await getOrder(orderId);
  while (!isSettled(order) && Date.now() < deadline) {
    await sleep(delay);
    delay = Math.min(delay * 1.5, 2000);
    order = await getOrder(orderId);
  }
  return order;
}

export async function getMyOrders(pageNumber = 1, pageSize = 10): Promise<OrderPage> {
  const { data } = await api.get<OrderPage>("/orders/my-orders", { params: { pageNumber, pageSize } });
  return data;
}

export type DisplayStatus = "Order Placed" | "Payment Declined" | "Cancelled" | "Processing";

const DECLINE_PREFIX = /^payment declined:?\s*/i;
const STOCK_PREFIX = /^out of stock:?\s*/i;

/** Map the stored order status to the status a customer sees. */
export function displayStatus(order: Pick<Order, "status" | "cancellationReason">): DisplayStatus {
  if (order.status === "Completed" || order.status === "Paid") return "Order Placed";
  if (order.status === "Cancelled") {
    return DECLINE_PREFIX.test(order.cancellationReason ?? "") ? "Payment Declined" : "Cancelled";
  }
  return "Processing";
}

/** Customer-friendly explanation of why an order was cancelled. */
export function cancellationMessage(order: Pick<Order, "cancellationReason">): string {
  const reason = order.cancellationReason?.trim() ?? "";
  if (DECLINE_PREFIX.test(reason)) return reason.replace(DECLINE_PREFIX, "") || "Your card was declined.";
  if (STOCK_PREFIX.test(reason)) return "Some items in your order are no longer in stock.";
  return reason || "This order was cancelled.";
}
