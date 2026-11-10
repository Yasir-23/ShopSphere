"use client";

import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { Check, Loader2, X } from "lucide-react";
import { useAuth } from "@/context/AuthContext";
import { getErrorMessage } from "@/lib/api";
import { AuthCard, Field, inputClass, safeNext } from "@/components/AuthCard";
import { cn } from "@/lib/utils";

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

// Mirrors the Identity service's password policy.
const PASSWORD_RULES = [
  { label: "At least 8 characters", test: (p: string) => p.length >= 8 },
  { label: "An uppercase letter", test: (p: string) => /[A-Z]/.test(p) },
  { label: "A lowercase letter", test: (p: string) => /[a-z]/.test(p) },
  { label: "A number", test: (p: string) => /[0-9]/.test(p) },
  { label: "A special character", test: (p: string) => /[^a-zA-Z0-9]/.test(p) },
];

export function RegisterForm() {
  const { register, user, ready } = useAuth();
  const router = useRouter();
  const next = useSearchParams().get("next");

  const [form, setForm] = useState({ firstName: "", lastName: "", email: "", password: "", confirm: "" });
  const [touched, setTouched] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (ready && user && !submitting) router.replace(safeNext(next));
  }, [ready, user, submitting, next, router]);

  const errors = {
    firstName: form.firstName.trim() ? undefined : "First name is required.",
    lastName: form.lastName.trim() ? undefined : "Last name is required.",
    email: !form.email.trim()
      ? "Email is required."
      : EMAIL_RE.test(form.email.trim())
        ? undefined
        : "Enter a valid email address.",
    password: PASSWORD_RULES.every((r) => r.test(form.password)) ? undefined : "Password doesn't meet the requirements below.",
    confirm: form.confirm && form.confirm === form.password ? undefined : "Passwords must match.",
  };
  const show = (k: keyof typeof errors) => (touched ? errors[k] : undefined);

  const set = (k: keyof typeof form) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm((f) => ({ ...f, [k]: e.target.value }));

  async function onSubmit(e: React.FormEvent) {
    e.preventDefault();
    setTouched(true);
    setError(null);
    if (Object.values(errors).some(Boolean)) return;
    setSubmitting(true);
    try {
      await register(form);
      router.replace(safeNext(next));
    } catch (err) {
      setError(getErrorMessage(err, "We couldn't create your account. Please try again."));
      setSubmitting(false);
    }
  }

  return (
    <AuthCard
      title="Create your account"
      subtitle="Save your cart, check out faster and track your orders."
      footer={
        <>
          Already have an account?{" "}
          <Link href={next ? `/login?next=${encodeURIComponent(next)}` : "/login"} className="font-semibold text-indigo-600 hover:text-indigo-500">
            Sign in
          </Link>
        </>
      }
    >
      <form onSubmit={onSubmit} noValidate className="space-y-4">
        {error && (
          <p role="alert" className="rounded-lg bg-rose-50 px-3 py-2.5 text-sm text-rose-700">
            {error}
          </p>
        )}
        <div className="grid grid-cols-2 gap-3">
          <Field label="First name" error={show("firstName")}>
            <input name="firstName" autoComplete="given-name" value={form.firstName} onChange={set("firstName")} className={inputClass(!!show("firstName"))} />
          </Field>
          <Field label="Last name" error={show("lastName")}>
            <input name="lastName" autoComplete="family-name" value={form.lastName} onChange={set("lastName")} className={inputClass(!!show("lastName"))} />
          </Field>
        </div>
        <Field label="Email" error={show("email")}>
          <input type="email" name="email" autoComplete="email" placeholder="you@example.com" value={form.email} onChange={set("email")} className={inputClass(!!show("email"))} />
        </Field>
        <Field label="Password" error={show("password")}>
          <input type="password" name="password" autoComplete="new-password" value={form.password} onChange={set("password")} className={inputClass(!!show("password"))} />
        </Field>
        <ul className="grid grid-cols-1 gap-1 text-xs sm:grid-cols-2" aria-label="Password requirements">
          {PASSWORD_RULES.map((r) => {
            const ok = r.test(form.password);
            return (
              <li key={r.label} className={cn("flex items-center gap-1.5", ok ? "text-emerald-600" : "text-slate-500")}>
                {ok ? <Check className="h-3.5 w-3.5" aria-hidden /> : <X className="h-3.5 w-3.5" aria-hidden />}
                {r.label}
              </li>
            );
          })}
        </ul>
        <Field label="Confirm password" error={show("confirm")}>
          <input type="password" name="confirm" autoComplete="new-password" value={form.confirm} onChange={set("confirm")} className={inputClass(!!show("confirm"))} />
        </Field>
        <button
          type="submit"
          disabled={submitting}
          className="flex w-full items-center justify-center gap-2 rounded-lg bg-indigo-600 py-2.5 text-sm font-semibold text-white shadow-sm hover:bg-indigo-500 disabled:opacity-60"
        >
          {submitting && <Loader2 className="h-4 w-4 animate-spin" aria-hidden />}
          {submitting ? "Creating account…" : "Create account"}
        </button>
      </form>
    </AuthCard>
  );
}
