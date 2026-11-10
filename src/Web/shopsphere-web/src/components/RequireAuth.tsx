"use client";

import Link from "next/link";
import { usePathname, useRouter } from "next/navigation";
import { useEffect } from "react";
import { Loader2, ShieldAlert } from "lucide-react";
import { useAuth } from "@/context/AuthContext";

/**
 * Client-side page guard. Guests are sent to sign in; signed-in users
 * without the admin role see an access notice on admin pages.
 * (The APIs enforce the same rules server-side.)
 */
export function RequireAuth({
  admin = false,
  children,
}: {
  admin?: boolean;
  children: React.ReactNode;
}) {
  const { user, ready } = useAuth();
  const router = useRouter();
  const pathname = usePathname();

  useEffect(() => {
    if (ready && !user) router.replace(`/login?next=${encodeURIComponent(pathname)}`);
  }, [ready, user, pathname, router]);

  if (!ready || !user) {
    return (
      <div className="flex justify-center py-32" role="status" aria-label="Loading">
        <Loader2 className="h-8 w-8 animate-spin text-indigo-600" />
      </div>
    );
  }

  if (admin && !user.isAdmin) {
    return (
      <div className="mx-auto flex max-w-md flex-col items-center px-4 py-24 text-center">
        <span className="flex h-14 w-14 items-center justify-center rounded-full bg-amber-50">
          <ShieldAlert className="h-7 w-7 text-amber-600" />
        </span>
        <h1 className="mt-4 text-xl font-bold text-slate-900">Admin access required</h1>
        <p className="mt-2 text-sm text-slate-500">
          You&apos;re signed in as {user.email}, which doesn&apos;t have permission to manage the store.
        </p>
        <Link href="/" className="mt-6 rounded-lg bg-indigo-600 px-4 py-2 text-sm font-semibold text-white hover:bg-indigo-500">
          Back to the store
        </Link>
      </div>
    );
  }

  return <>{children}</>;
}
