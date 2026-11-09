import axios from "axios";

export const API_BASE_URL =
  process.env.NEXT_PUBLIC_GATEWAY_URL || "http://localhost:5001/api/v1";

/**
 * Safely retrieve the active auth token from localStorage.
 * Checks both the direct "token" key and "shopsphere_user" JSON payload.
 */
export function getStoredToken(): string | null {
  if (typeof window === "undefined") return null;
  try {
    const directToken = localStorage.getItem("token");
    if (directToken && directToken.trim()) {
      return directToken.trim();
    }

    const storedUser = localStorage.getItem("shopsphere_user");
    if (storedUser) {
      const parsed = JSON.parse(storedUser);
      const token = parsed?.token || parsed?.accessToken || null;
      if (token) {
        // Keep "token" in sync
        localStorage.setItem("token", token);
        return token;
      }
    }
  } catch (e) {
    console.error("Failed to read token from localStorage:", e);
  }
  return null;
}

export const api = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    "Content-Type": "application/json",
  },
});

// Request interceptor: dynamically read token on EVERY outgoing request
api.interceptors.request.use(
  (config) => {
    const token = getStoredToken();
    if (token) {
      if (config.headers && typeof config.headers.set === "function") {
        config.headers.set("Authorization", `Bearer ${token}`);
      } else {
        config.headers = config.headers || {};
        config.headers["Authorization"] = `Bearer ${token}`;
      }
    }
    return config;
  },
  (error) => Promise.reject(error)
);

export default api;
