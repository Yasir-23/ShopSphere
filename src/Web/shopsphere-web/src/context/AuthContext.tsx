"use client";

import React, { createContext, useContext, useState, useEffect, useCallback } from "react";
import { api } from "@/lib/api";

export interface User {
  email: string;
  role: string;
  token: string;
  firstName?: string;
  lastName?: string;
}

interface AuthContextType {
  user: User | null;
  loading: boolean;
  error: string | null;
  login: (email: string, password: string, defaultRole?: string) => Promise<boolean>;
  loginAsCustomer: () => Promise<boolean>;
  loginAsAdmin: () => Promise<boolean>;
  logout: () => void;
  clearError: () => void;
}

const STORAGE_KEY = "shopsphere_user";

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [user, setUser] = useState<User | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [error, setError] = useState<string | null>(null);

  // Initialize from localStorage on mount
  useEffect(() => {
    try {
      const stored = localStorage.getItem(STORAGE_KEY);
      if (stored) {
        const parsed = JSON.parse(stored);
        if (parsed?.token && parsed?.email) {
          setUser(parsed);
        }
      }
    } catch (err) {
      console.error("Failed to restore auth session:", err);
    } finally {
      setLoading(false);
    }
  }, []);

  const login = useCallback(
    async (email: string, password: string, defaultRole = "Customer"): Promise<boolean> => {
      setLoading(true);
      setError(null);
      try {
        const response = await api.post("/identity/login", {
          email,
          password,
        });

        const token = response.data?.accessToken;
        if (!token) {
          throw new Error("No access token received from authentication service.");
        }

        let role = defaultRole;
        let firstName = "";
        let lastName = "";

        // Attempt to fetch profile to resolve exact roles and names
        try {
          const profileRes = await api.get("/identity/me", {
            headers: { Authorization: `Bearer ${token}` },
          });
          if (profileRes.data?.roles && profileRes.data.roles.length > 0) {
            role = profileRes.data.roles[0];
          }
          firstName = profileRes.data?.firstName || "";
          lastName = profileRes.data?.lastName || "";
        } catch {
          // If /identity/me fails, defaultRole is preserved
          console.warn("Could not fetch user profile details, using default role.");
        }

        const newUser: User = {
          email,
          role,
          token,
          firstName,
          lastName,
        };

        setUser(newUser);
        localStorage.setItem(STORAGE_KEY, JSON.stringify(newUser));
        return true;
      } catch (err: any) {
        const msg =
          err?.response?.data?.detail ||
          err?.response?.data?.title ||
          err?.message ||
          "Authentication failed. Please verify credentials.";
        setError(msg);
        return false;
      } finally {
        setLoading(false);
      }
    },
    []
  );

  const loginAsCustomer = useCallback(() => {
    return login("customer@shopsphere.local", "SecurePassword123!", "Customer");
  }, [login]);

  const loginAsAdmin = useCallback(() => {
    return login("admin@shopsphere.local", "AdminPassword123!", "Admin");
  }, [login]);

  const logout = useCallback(() => {
    setUser(null);
    setError(null);
    try {
      localStorage.removeItem(STORAGE_KEY);
    } catch (err) {
      console.error("Failed to clear local storage:", err);
    }
  }, []);

  const clearError = useCallback(() => {
    setError(null);
  }, []);

  return (
    <AuthContext.Provider
      value={{
        user,
        loading,
        error,
        login,
        loginAsCustomer,
        loginAsAdmin,
        logout,
        clearError,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
};

export const useAuth = (): AuthContextType => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error("useAuth must be used within an AuthProvider");
  }
  return context;
};

