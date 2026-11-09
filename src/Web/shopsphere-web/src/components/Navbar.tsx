"use client";

import React, { useEffect, useState, useCallback } from "react";
import { useAuth } from "@/context/AuthContext";
import { checkGatewayHealth } from "@/lib/catalog";
import { 
  ShoppingBag, 
  Activity, 
  User as UserIcon, 
  ShieldCheck, 
  LogOut, 
  RefreshCw,
  AlertCircle
} from "lucide-react";

export const Navbar: React.FC = () => {
  const { user, loginAsCustomer, loginAsAdmin, logout, loading: authLoading, error, clearError } = useAuth();
  const [gatewayOnline, setGatewayOnline] = useState<boolean | null>(null);
  const [checkingHealth, setCheckingHealth] = useState<boolean>(false);

  const verifyHealth = useCallback(async () => {
    setCheckingHealth(true);
    try {
      const isHealthy = await checkGatewayHealth();
      setGatewayOnline(isHealthy);
    } catch {
      setGatewayOnline(false);
    } finally {
      setCheckingHealth(false);
    }
  }, []);

  useEffect(() => {
    verifyHealth();
    const interval = setInterval(verifyHealth, 15000);
    return () => clearInterval(interval);
  }, [verifyHealth]);

  return (
    <header className="sticky top-0 z-50 w-full border-b border-slate-200 bg-white/95 backdrop-blur shadow-sm dark:border-slate-800 dark:bg-slate-900/95">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="flex h-16 items-center justify-between gap-4">
          
          {/* Logo & Platform Info */}
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-gradient-to-tr from-indigo-600 to-violet-500 text-white shadow-md shadow-indigo-500/20">
              <ShoppingBag className="h-5 w-5" />
            </div>
            <div>
              <div className="flex items-center gap-2">
                <span className="text-xl font-black tracking-tight bg-gradient-to-r from-slate-900 via-indigo-950 to-indigo-700 bg-clip-text text-transparent dark:from-white dark:to-indigo-300">
                  ShopSphere
                </span>
                <span className="rounded-md bg-indigo-50 px-2 py-0.5 text-[11px] font-semibold text-indigo-700 border border-indigo-200/60 dark:bg-indigo-950 dark:text-indigo-300 dark:border-indigo-800">
                  YARP Gateway v1
                </span>
              </div>
              <p className="text-xs text-slate-500 dark:text-slate-400 hidden sm:block">
                Distributed .NET 8 Microservices Showcase
              </p>
            </div>
          </div>

          {/* Gateway Status & User Controls */}
          <div className="flex items-center gap-3 sm:gap-4">
            
            {/* Live Gateway Connection Indicator */}
            <button
              onClick={verifyHealth}
              disabled={checkingHealth}
              title="Click to re-check API Gateway health at http://localhost:5001"
              className="flex items-center gap-2 rounded-lg border border-slate-200 bg-slate-50/80 px-2.5 py-1.5 text-xs font-medium text-slate-700 transition hover:bg-slate-100 dark:border-slate-800 dark:bg-slate-800/80 dark:text-slate-300 dark:hover:bg-slate-800"
            >
              <span className="relative flex h-2.5 w-2.5">
                {gatewayOnline === true && (
                  <>
                    <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-emerald-400 opacity-75"></span>
                    <span className="relative inline-flex h-2.5 w-2.5 rounded-full bg-emerald-500"></span>
                  </>
                )}
                {gatewayOnline === false && (
                  <span className="relative inline-flex h-2.5 w-2.5 rounded-full bg-rose-500"></span>
                )}
                {gatewayOnline === null && (
                  <span className="relative inline-flex h-2.5 w-2.5 rounded-full bg-amber-400"></span>
                )}
              </span>
              <span className="hidden md:inline">Gateway (5001):</span>
              <span className="font-semibold">
                {gatewayOnline === true ? "Online" : gatewayOnline === false ? "Offline" : "Checking..."}
              </span>
              <RefreshCw className={`h-3 w-3 text-slate-400 ${checkingHealth ? "animate-spin" : ""}`} />
            </button>

            {/* Active User Badge */}
            <div className="flex items-center gap-2">
              {user ? (
                <div className="flex items-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-3 py-1 dark:border-slate-800 dark:bg-slate-800/60">
                  <span
                    className={`rounded-full px-2 py-0.5 text-[10px] font-bold uppercase tracking-wider ${
                      user.role.toLowerCase().includes("admin")
                        ? "bg-purple-100 text-purple-700 border border-purple-200 dark:bg-purple-950/80 dark:text-purple-300 dark:border-purple-800"
                        : "bg-blue-100 text-blue-700 border border-blue-200 dark:bg-blue-950/80 dark:text-blue-300 dark:border-blue-800"
                    }`}
                  >
                    {user.role}
                  </span>
                  <span className="text-xs font-medium text-slate-800 dark:text-slate-200 max-w-[160px] truncate" title={user.email}>
                    {user.email}
                  </span>
                </div>
              ) : (
                <div className="flex items-center gap-1.5 rounded-full border border-slate-200 bg-slate-100 px-3 py-1 text-xs font-medium text-slate-600 dark:border-slate-800 dark:bg-slate-800 dark:text-slate-400">
                  <UserIcon className="h-3.5 w-3.5 text-slate-400" />
                  <span>Guest</span>
                </div>
              )}
            </div>

            {/* Instant Login Shortcuts */}
            <div className="flex items-center gap-1.5">
              <button
                onClick={() => loginAsCustomer()}
                disabled={authLoading}
                className="inline-flex items-center gap-1.5 rounded-lg bg-blue-600 px-3 py-1.5 text-xs font-medium text-white shadow-sm hover:bg-blue-500 focus-visible:outline focus-visible:outline-2 focus-visible:outline-blue-600 disabled:opacity-50 transition"
              >
                <UserIcon className="h-3.5 w-3.5" />
                <span className="hidden sm:inline">Customer</span>
              </button>

              <button
                onClick={() => loginAsAdmin()}
                disabled={authLoading}
                className="inline-flex items-center gap-1.5 rounded-lg bg-purple-600 px-3 py-1.5 text-xs font-medium text-white shadow-sm hover:bg-purple-500 focus-visible:outline focus-visible:outline-2 focus-visible:outline-purple-600 disabled:opacity-50 transition"
              >
                <ShieldCheck className="h-3.5 w-3.5" />
                <span className="hidden sm:inline">Admin</span>
              </button>

              {user && (
                <button
                  onClick={logout}
                  title="Log out"
                  className="inline-flex items-center gap-1 rounded-lg border border-slate-300 bg-white px-2.5 py-1.5 text-xs font-medium text-slate-700 shadow-sm hover:bg-slate-50 dark:border-slate-700 dark:bg-slate-800 dark:text-slate-300 dark:hover:bg-slate-700 transition"
                >
                  <LogOut className="h-3.5 w-3.5" />
                  <span className="hidden md:inline">Logout</span>
                </button>
              )}
            </div>

          </div>
        </div>

        {/* Global Error Notice if Login / Gateway fails */}
        {error && (
          <div className="mb-2 flex items-center justify-between rounded-lg border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700 dark:border-red-900/60 dark:bg-red-950/40 dark:text-red-300">
            <div className="flex items-center gap-2">
              <AlertCircle className="h-4 w-4 shrink-0 text-red-500" />
              <span>{error}</span>
            </div>
            <button
              onClick={clearError}
              className="ml-2 font-semibold text-red-800 hover:underline dark:text-red-200"
            >
              Dismiss
            </button>
          </div>
        )}
      </div>
    </header>
  );
};

export default Navbar;

