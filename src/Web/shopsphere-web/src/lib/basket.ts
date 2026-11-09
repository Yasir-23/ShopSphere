import { api, getStoredToken } from "./api";

export interface BasketItem {
  productId: string;
  productName: string;
  unitPrice: number;
  quantity: number;
  totalPrice?: number;
  imageUrl?: string;
  category?: string;
}

export interface CustomerBasket {
  customerId: string;
  items: BasketItem[];
  totalAmount: number;
}

export interface UpsertBasketItem {
  productId: string;
  productName: string;
  unitPrice?: number;
  quantity: number;
}

/**
 * Fetch the authenticated customer's cart from Redis via API Gateway.
 * If unauthenticated or token expired, gracefully returns null without throwing.
 */
export async function getBasket(): Promise<CustomerBasket | null> {
  const token = getStoredToken();
  if (!token) {
    // Gracefully ignore when unauthenticated
    return null;
  }

  try {
    const response = await api.get<CustomerBasket>("/basket", {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    });
    return response.data;
  } catch (error: any) {
    const status = error?.response?.status;
    if (status === 401 || status === 403 || status === 404) {
      console.warn(`[Basket] Session unauthenticated or basket empty (HTTP ${status}). Keeping local state.`);
      return null;
    }
    console.warn("[Basket] Failed to fetch customer basket from Redis:", error?.message || error);
    return null;
  }
}

/**
 * Save or update basket items in Redis via API Gateway.
 * Backend verifies prices against Catalog Service over gRPC.
 */
export async function updateBasket(
  items: UpsertBasketItem[]
): Promise<CustomerBasket | null> {
  const token = getStoredToken();
  if (!token) {
    console.warn("[Basket] Skipping Redis update: User is not authenticated.");
    return null;
  }

  try {
    const response = await api.post<CustomerBasket>(
      "/basket",
      {
        items: items.map((i) => ({
          productId: i.productId,
          productName: i.productName,
          unitPrice: i.unitPrice ?? 0,
          quantity: i.quantity,
        })),
      },
      {
        headers: {
          Authorization: `Bearer ${token}`,
        },
      }
    );
    return response.data;
  } catch (error: any) {
    const status = error?.response?.status;
    if (status === 401 || status === 403) {
      console.warn(`[Basket] Token expired or invalid during update (HTTP ${status}).`);
      return null;
    }
    console.warn("[Basket] Failed to update basket in Redis:", error?.message || error);
    return null;
  }
}

/**
 * Delete and clear the customer's cart from Redis.
 */
export async function clearBasket(): Promise<boolean> {
  const token = getStoredToken();
  if (!token) {
    return false;
  }

  try {
    await api.delete("/basket", {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    });
    return true;
  } catch (error: any) {
    const status = error?.response?.status;
    if (status === 401 || status === 404) {
      return false;
    }
    console.warn("[Basket] Failed to clear Redis basket:", error?.message || error);
    return false;
  }
}

export interface ShippingAddress {
  street: string;
  city: string;
  state: string;
  zipCode: string;
  country: string;
}

export interface CreateOrderPayload {
  shippingAddress: ShippingAddress;
  items: {
    productId: string;
    productName: string;
    unitPrice: number;
    quantity: number;
  }[];
  customerId?: string;
  customerEmail?: string;
}

export interface OrderItem {
  id: string;
  productId: string;
  productName: string;
  unitPrice: number;
  quantity: number;
  totalPrice: number;
}

export interface OrderResponse {
  id: string;
  customerId: string;
  customerEmail: string;
  shippingAddress: ShippingAddress;
  items: OrderItem[];
  totalAmount: number;
  status: string;
  createdAtUtc: string;
  updatedAtUtc?: string;
}

export interface PaymentProcessResponse {
  id: string;
  orderId: string;
  amount: number;
  currency: string;
  status: "Completed" | "Failed" | "Pending" | "Processing";
  paymentMethod: string;
  transactionId?: string;
  failureReason?: string;
  createdAtUtc: string;
}

/**
 * Create a new order via Gateway (Ordering Service).
 * Attaches the active JWT Bearer token and returns a clear error message on 502/500 gateway errors.
 */
export async function createOrder(
  payload: CreateOrderPayload
): Promise<OrderResponse> {
  const token = getStoredToken();
  if (!token) {
    console.warn("[Ordering] createOrder halted: No active JWT token found.");
    throw new Error("Authentication token is missing. Please log in before placing an order.");
  }

  try {
    const response = await api.post<OrderResponse>("/orders", payload, {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    });
    return response.data;
  } catch (error: any) {
    const status = error?.response?.status;
    console.error("[Ordering] createOrder error:", status, error?.message || error);

    if (status === 502 || status === 503 || error?.code === "ERR_BAD_RESPONSE") {
      throw new Error(
        "Gateway could not reach Ordering Service (HTTP 502). Please ensure the service is running on port 5040."
      );
    }
    if (status === 500) {
      const detail =
        error?.response?.data?.detail ||
        error?.response?.data?.title ||
        error?.response?.data?.message;
      throw new Error(
        detail || "Gateway could not reach Ordering Service (HTTP 502). Please ensure the service is running on port 5040."
      );
    }
    if (status === 401 || status === 403) {
      throw new Error("Your session has expired. Please log in again.");
    }

    const message =
      error?.response?.data?.detail ||
      error?.response?.data?.title ||
      error?.message ||
      "Failed to place order.";
    throw new Error(message);
  }
}

/**
 * Retrieve an order by ID via Gateway (Ordering Service).
 */
export async function getOrder(orderId: string): Promise<OrderResponse | null> {
  const token = getStoredToken();
  if (!token) return null;
  try {
    const response = await api.get<OrderResponse>(`/orders/${orderId}`, {
      headers: {
        Authorization: `Bearer ${token}`,
      },
    });
    return response.data;
  } catch (error: any) {
    console.warn(`[Ordering] Failed to fetch order ${orderId}:`, error?.message || error);
    return null;
  }
}

/**
 * Process order payment via Gateway (Payment Service / Stripe / Simulator).
 * Supplies complete request payload with standard payment simulation attributes.
 */
export async function processPayment(
  orderId: string,
  amount: number,
  currency = "USD",
  paymentMethod: string,
  cardDetails?: { cardNumber?: string; cvv?: string; expiryMonth?: number; expiryYear?: number }
): Promise<PaymentProcessResponse> {
  const token = getStoredToken();
  const numericAmount = Number(amount) || 0;
  const isDecline = paymentMethod.includes("0000");
  const cardNumber = cardDetails?.cardNumber || (isDecline ? "0000000000000000" : "4242424242424242");

  const payload = {
    orderId,
    amount: numericAmount,
    currency: (currency || "USD").toUpperCase(),
    paymentMethod,
    cardNumber,
    cvv: cardDetails?.cvv || "123",
    expiryMonth: cardDetails?.expiryMonth || 12,
    expiryYear: cardDetails?.expiryYear || 2028,
  };

  try {
    const response = await api.post<PaymentProcessResponse>(
      "/payments",
      payload,
      token
        ? {
            headers: {
              Authorization: `Bearer ${token}`,
            },
          }
        : undefined
    );
    return response.data;
  } catch (error: any) {
    const status = error?.response?.status;
    if (status === 502 || status === 503) {
      throw new Error(
        "Gateway could not reach Payment Service (HTTP 502). Please ensure the service is running on port 5050."
      );
    }
    throw error;
  }
}
