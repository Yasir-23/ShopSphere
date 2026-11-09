import { api, API_BASE_URL } from "./api";

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
  imageUrl?: string;
  isAvailable: boolean;
  isActive: boolean;
  createdAtUtc?: string;
  updatedAtUtc?: string;
}

export interface PaginatedResult<T> {
  items: T[];
  pageNumber: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
}

export interface InventoryStock {
  id?: string;
  productId: string;
  sku: string;
  quantityOnHand: number;
  quantityReserved: number;
  quantityAvailable: number;
  availableQuantity?: number;
  reservedQuantity?: number;
  restockThreshold?: number;
  updatedAtUtc?: string;
}

/**
 * Fetch products list from Catalog service via Gateway.
 */
export async function getProducts(
  pageNumber = 1,
  pageSize = 20
): Promise<Product[]> {
  try {
    const response = await api.get<PaginatedResult<Product> | Product[]>(
      "/catalog/products",
      {
        params: { pageNumber, pageSize },
      }
    );

    if (response.data && "items" in response.data && Array.isArray(response.data.items)) {
      return response.data.items;
    }
    if (Array.isArray(response.data)) {
      return response.data;
    }
    return [];
  } catch (error) {
    console.error("Failed to fetch products from gateway:", error);
    throw error;
  }
}

/**
 * Fetch stock availability for a product from Inventory service via Gateway.
 */
export async function getStock(productId: string): Promise<InventoryStock | null> {
  try {
    const response = await api.get<InventoryStock>(`/inventory/${encodeURIComponent(productId)}`);
    return response.data;
  } catch (error: any) {
    if (error?.response?.status === 404) {
      return null;
    }
    console.error(`Failed to fetch inventory stock for product ${productId}:`, error);
    return null;
  }
}

/**
 * Check connectivity to the YARP Gateway health endpoint.
 */
export async function checkGatewayHealth(): Promise<boolean> {
  try {
    const baseGatewayHost = API_BASE_URL.replace(/\/api\/v1\/?$/, "");
    const response = await api.get("/health", {
      baseURL: baseGatewayHost || "http://localhost:5001",
      timeout: 3500,
    });
    return response.status === 200;
  } catch {
    return false;
  }
}

