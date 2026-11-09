import axios from "axios";

export const API_BASE_URL =
  process.env.NEXT_PUBLIC_GATEWAY_URL || "http://localhost:5001/api/v1";

export const api = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    "Content-Type": "application/json",
  },
});

api.interceptors.request.use(
  (config) => {
    if (typeof window !== "undefined") {
      const storedUser = localStorage.getItem("shopsphere_user");
      if (storedUser) {
        try {
          const parsed = JSON.parse(storedUser);
          if (parsed?.token) {
            config.headers.Authorization = `Bearer ${parsed.token}`;
          }
        } catch (e) {
          console.error("Failed to parse stored user token", e);
        }
      }
    }
    return config;
  },
  (error) => Promise.reject(error)
);

export default api;

