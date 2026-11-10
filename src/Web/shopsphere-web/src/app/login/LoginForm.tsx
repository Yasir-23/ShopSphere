"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { Eye, EyeOff, Loader2, ShieldCheck, User } from "lucide-react";
import { DEMO_ACCOUNTS, useAuth } from "@/context/AuthContext";
import { getErrorMessage } from "@/lib/api";
import { AuthCard, Field, inputClass, safeNext } from "@/components/AuthCard";

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export function LoginForm() {
  const { login, user, ready } = useAuth();
  const router = useRouter();
  const searchParams = useSearchParams();
  const next = searchParams.get("next");

  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [touched, setTouched] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Already signed in (e.g. navigated here manually): continue on.
  useEffect(() => {
    if (ready && user && !submitting) router.replace(safeNext(next, user.isAdmin ? "/admin" : "/"));
  }, [ready, user, submitting, next, router]);

  const emailError = !email.trim() ? "Email is required." : !EMAIL_RE.test(email.trim()) ? "Enter a valid email address." : undefined;
  const passwordError = !password ? "Password is required." : undefined;

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setTouched(true);
    setError(null);
    if (emailError || passwordError) return;
    setSubmitting(true);
    try {
      const signedIn = await login(email, password);
      router.replace(safeNext(next, signedIn.isAdmin ? "/admin" : "/"));
    } catch (err) {
      setError(getErrorMessage(err, "We couldn't sign you in. Please try again."));
      setSubmitting(false);
    }
  }

  const fill = (account: keyof typeof DEMO_ACCOUNTS) => {
    setEmail(DEMO_ACCOUNTS[account].email);
    setPassword(DEMO_ACCOUNTS[account].password);
    setError(null);
  };

  return (
    <AuthCard
      title="Welcome back"
      subtitle="Sign in to check out, view your orders and sync your cart."
      footer={
        <>
          New to ShopSphere?{" "}
          <Link href={next ? `/register?next=${encodeURIComponent(next)}` : "/register"} className="font-semibold text-indigo-600 hover:text-indigo-500">
            Create an account
          </Link>
        </>
      }
    >
      <div className="mb-6 rounded-xl border border-dashed border-indigo-200 bg-indigo-50/60 p-3">
        <p className="text-xs font-semibold uppercase tracking-wide text-indigo-700">Demo accounts</p>
        <div className="mt-2 grid grid-cols-2 gap-2">
          <button
            type="button"
            onClick={() => fill("customer")}
            className="inline-flex items-center justify-center gap-1.5 rounded-lg bg-white px-3 py-2 text-xs font-semibold text-slate-700 shadow-sm ring-1 ring-slate-200 hover:bg-slate-50"
          >
            <User className="h-3.5 w-3.5" aria-hidden /> Fill customer credentials
          </button>
          <button
            type="button"
            onClick={() => fill("admin")}
            className="inline-flex items-center justify-center gap-1.5 rounded-lg bg-white px-3 py-2 text-xs font-semibold text-slate-700 shadow-sm ring-1 ring-slate-200 hover:bg-slate-50"
          >
            <ShieldCheck className="h-3.5 w-3.5" aria-hidden /> Fill admin credentials
          </button>
        </div>
      </div>

      <form onSubmit={onSubmit} noValidate className="space-y-4">
        {error && (
          <p role="alert" className="rounded-lg bg-rose-50 px-3 py-2.5 text-sm text-rose-700">
            {error}
          </p>
        )}
        <Field label="Email" error={touched ? emailError : undefined}>
          <input
            type="email"
            name="email"
            autoComplete="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            placeholder="you@example.com"
            aria-invalid={touched && !!emailError}
            className={inputClass(touched && !!emailError)}
          />
        </Field>
        <Field label="Password" error={touched ? passwordError : undefined}>
          <div className="relative">
            <input
              type={showPassword ? "text" : "password"}
              name="password"
              autoComplete="current-password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              aria-invalid={touched && !!passwordError}
              className={inputClass(touched && !!passwordError) + " pr-10"}
            />
            <button
              type="button"
              onClick={() => setShowPassword((s) => !s)}
              className="absolute right-2 top-1/2 -translate-y-1/2 rounded p-1 text-slate-400 hover:text-slate-600"
              aria-label={showPassword ? "Hide password" : "Show password"}
            >
              {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
            </button>
          </div>
        </Field>
        <button
          type="submit"
          disabled={submitting}
          className="flex w-full items-center justify-center gap-2 rounded-lg bg-indigo-600 py-2.5 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 disabled:opacity-60"
        >
          {submitting && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />}
          {submitting ? "Signing in…" : "Sign in"}
        </button>
      </form>
    </AuthCard>
  );
}
