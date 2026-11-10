import axios, { AxiosError, type InternalAxiosRequestConfig } from "axios";
import { getSession, setSession } from "./session";

export const API_BASE_URL =
  process.env.NEXT_PUBLIC_GATEWAY_URL || "http://localhost:5001/api/v1";

export const api = axios.create({
  baseURL: API_BASE_URL,
  headers: { "Content-Type": "application/json" },
  timeout: 20000,
});

// Attach the current Bearer token to every outgoing request.
api.interceptors.request.use((config) => {
  const token = getSession()?.accessToken;
  if (token && !config.headers.has("Authorization")) {
    config.headers.set("Authorization", `Bearer ${token}`);
  }
  return config;
});

// Single-flight refresh so parallel 401s trigger only one refresh call.
let refreshInFlight: Promise<string | null> | null = null;

async function refreshAccessToken(): Promise<string | null> {
  const session = getSession();
  if (!session?.refreshToken) return null;
  try {
    const { data } = await axios.post<{ accessToken: string; refreshToken: string; expiresIn: number }>(
      `${API_BASE_URL}/identity/refresh`,
      { refreshToken: session.refreshToken },
      { headers: { "Content-Type": "application/json" } }
    );
    setSession({
      ...session,
      accessToken: data.accessToken,
      refreshToken: data.refreshToken,
      expiresAt: Date.now() + data.expiresIn * 1000,
    });
    return data.accessToken;
  } catch {
    setSession(null);
    return null;
  }
}

type RetriableConfig = InternalAxiosRequestConfig & { _retried?: boolean };

api.interceptors.response.use(
  (response) => response,
  async (error: AxiosError) => {
    const config = error.config as RetriableConfig | undefined;
    const isAuthCall = config?.url?.startsWith("/identity/");
    if (error.response?.status === 401 && config && !config._retried && !isAuthCall && getSession()) {
      config._retried = true;
      refreshInFlight ??= refreshAccessToken().finally(() => {
        refreshInFlight = null;
      });
      const token = await refreshInFlight;
      if (token) {
        config.headers.set("Authorization", `Bearer ${token}`);
        return api.request(config);
      }
    }
    return Promise.reject(error);
  }
);

interface ProblemDetails {
  title?: string;
  detail?: string;
  errors?: Record<string, string[]>;
}

/** Turn any API error into a short, customer-friendly message. */
export function getErrorMessage(error: unknown, fallback = "Something went wrong. Please try again."): string {
  if (axios.isAxiosError(error)) {
    if (!error.response) {
      return "We couldn't reach the store right now. Please check your connection and try again.";
    }
    const status = error.response.status;
    const data = error.response.data as ProblemDetails | undefined;
    if (data?.errors) {
      const first = Object.values(data.errors).flat()[0];
      if (first) return first;
    }
    if (status === 401) return data?.detail || "Please sign in to continue.";
    if (status === 403) return "You don't have permission to do that.";
    if (status === 429) return "Too many requests. Please wait a moment and try again.";
    if (status >= 500) return "The store is temporarily unavailable. Please try again shortly.";
    return data?.detail || data?.title || fallback;
  }
  if (error instanceof Error && error.message) return error.message;
  return fallback;
}

export default api;
