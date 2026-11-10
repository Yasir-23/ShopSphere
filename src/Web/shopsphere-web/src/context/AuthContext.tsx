"use client";

import React, { createContext, useCallback, useContext, useMemo, useSyncExternalStore } from "react";
import { api } from "@/lib/api";
import { getServerSession, getSession, isAdminRole, setSession, subscribeSession, type Session } from "@/lib/session";

export interface AuthUser {
  id: string;
  email: string;
  firstName: string;
  lastName: string;
  roles: string[];
  isAdmin: boolean;
}

export interface RegisterInput {
  firstName: string;
  lastName: string;
  email: string;
  password: string;
}

interface AuthActions {
  login: (email: string, password: string) => Promise<AuthUser>;
  register: (input: RegisterInput) => Promise<AuthUser>;
  logout: () => void;
}

interface AuthContextValue extends AuthActions {
  user: AuthUser | null;
  /** False until the browser session has been read; guards must wait for it. */
  ready: boolean;
}

export const DEMO_ACCOUNTS = {
  customer: { email: "customer@shopsphere.local", password: "SecurePassword123!" },
  admin: { email: "admin@shopsphere.local", password: "AdminPassword123!" },
} as const;

// The context carries only actions. Session state is read with
// useSyncExternalStore inside useAuth() so every component hydrates against the
// server snapshot (signed out) on its own schedule — including late-hydrating
// Suspense boundaries — and avoids hydration mismatches.
const AuthContext = createContext<AuthActions | undefined>(undefined);

const noopSubscribe = () => () => {};

function toUser(s: Session): AuthUser {
  return {
    id: s.userId,
    email: s.email,
    firstName: s.firstName,
    lastName: s.lastName,
    roles: s.roles,
    isAdmin: isAdminRole(s.roles),
  };
}

export function AuthProvider({ children }: { children: React.ReactNode }) {
  const login = useCallback(async (email: string, password: string) => {
    const { data: tokens } = await api.post<{ accessToken: string; refreshToken: string; expiresIn: number }>(
      "/identity/login",
      { email: email.trim(), password }
    );
    const { data: profile } = await api.get<{
      id: string;
      email: string;
      firstName: string;
      lastName: string;
      roles: string[];
    }>("/identity/me", { headers: { Authorization: `Bearer ${tokens.accessToken}` } });

    const next: Session = {
      accessToken: tokens.accessToken,
      refreshToken: tokens.refreshToken,
      expiresAt: Date.now() + tokens.expiresIn * 1000,
      userId: profile.id,
      email: profile.email,
      firstName: profile.firstName,
      lastName: profile.lastName,
      roles: profile.roles ?? [],
    };
    setSession(next);
    return toUser(next);
  }, []);

  const register = useCallback(
    async (input: RegisterInput) => {
      await api.post("/identity/register", {
        firstName: input.firstName.trim(),
        lastName: input.lastName.trim(),
        email: input.email.trim(),
        password: input.password,
      });
      return login(input.email, input.password);
    },
    [login]
  );

  const logout = useCallback(() => {
    const refreshToken = getSession()?.refreshToken;
    if (refreshToken) {
      api.post("/identity/revoke", { refreshToken }).catch(() => undefined);
    }
    setSession(null);
  }, []);

  const actions = useMemo<AuthActions>(() => ({ login, register, logout }), [login, register, logout]);

  return <AuthContext.Provider value={actions}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const actions = useContext(AuthContext);
  if (!actions) throw new Error("useAuth must be used within an AuthProvider");
  const session = useSyncExternalStore(subscribeSession, getSession, getServerSession);
  const ready = useSyncExternalStore(noopSubscribe, () => true, () => false);
  const user = useMemo(() => (session ? toUser(session) : null), [session]);
  return { ...actions, user, ready };
}
