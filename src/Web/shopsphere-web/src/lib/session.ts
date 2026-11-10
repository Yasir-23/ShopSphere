/**
 * Browser session store (localStorage-backed) exposed through a
 * useSyncExternalStore-compatible API so React renders stay hydration-safe:
 * the server snapshot is always `null`, the client snapshot is read lazily.
 */

export interface Session {
  accessToken: string;
  refreshToken: string;
  /** Epoch milliseconds when the access token expires. */
  expiresAt: number;
  userId: string;
  email: string;
  firstName: string;
  lastName: string;
  roles: string[];
}

const STORAGE_KEY = "shopsphere_session";
const CHANGE_EVENT = "shopsphere:session";

let cachedRaw: string | null | undefined;
let cachedSession: Session | null = null;

function readRaw(): string | null {
  try {
    return localStorage.getItem(STORAGE_KEY);
  } catch {
    return null;
  }
}

export function getSession(): Session | null {
  if (typeof window === "undefined") return null;
  const raw = readRaw();
  if (raw === cachedRaw) return cachedSession;
  cachedRaw = raw;
  try {
    const parsed = raw ? (JSON.parse(raw) as Session) : null;
    cachedSession = parsed?.accessToken && parsed?.email ? parsed : null;
  } catch {
    cachedSession = null;
  }
  return cachedSession;
}

export function setSession(session: Session | null): void {
  try {
    if (session) localStorage.setItem(STORAGE_KEY, JSON.stringify(session));
    else localStorage.removeItem(STORAGE_KEY);
  } catch {
    // Storage can be unavailable (private mode); the session simply won't persist.
  }
  window.dispatchEvent(new Event(CHANGE_EVENT));
}

export function subscribeSession(callback: () => void): () => void {
  const onStorage = (e: StorageEvent) => {
    if (e.key === STORAGE_KEY) callback();
  };
  window.addEventListener(CHANGE_EVENT, callback);
  window.addEventListener("storage", onStorage);
  return () => {
    window.removeEventListener(CHANGE_EVENT, callback);
    window.removeEventListener("storage", onStorage);
  };
}

export const getServerSession = (): Session | null => null;

export function isAdminRole(roles: string[] | undefined): boolean {
  return !!roles?.some((r) => r === "Admin" || r === "Administrator");
}
