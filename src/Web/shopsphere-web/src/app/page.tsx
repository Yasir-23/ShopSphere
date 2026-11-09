import React from "react";
import Navbar from "@/components/Navbar";
import ProductGrid from "@/components/ProductGrid";
import { Server, Database, Shield, Zap } from "lucide-react";

export default function HomePage() {
  return (
    <div className="min-h-screen flex flex-col bg-slate-50 dark:bg-slate-950">
      {/* Global Navigation & Auth Bar */}
      <Navbar />

      {/* Hero / Architecture Banner */}
      <section className="border-b border-slate-200/80 bg-white py-8 shadow-xs dark:border-slate-800 dark:bg-slate-900/50">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="flex flex-col md:flex-row md:items-center justify-between gap-6">
            <div>
              <div className="inline-flex items-center gap-1.5 rounded-full bg-indigo-50 px-3 py-1 text-xs font-semibold text-indigo-700 border border-indigo-200 dark:bg-indigo-950 dark:text-indigo-300 dark:border-indigo-800 mb-3">
                <Zap className="h-3.5 w-3.5 text-indigo-600 dark:text-indigo-400" />
                <span>Part 1: Setup, Auth, & Live Catalog</span>
              </div>
              <h1 className="text-3xl font-extrabold tracking-tight text-slate-900 sm:text-4xl dark:text-white">
                ShopSphere Microservices Showcase
              </h1>
              <p className="mt-2 max-w-3xl text-sm sm:text-base text-slate-600 dark:text-slate-400 leading-relaxed">
                A lightweight, modern Next.js frontend communicating through the central{" "}
                <span className="font-semibold text-slate-900 dark:text-white">YARP API Gateway</span> (Port 5001). 
                Consuming Catalog from MongoDB, live Stock from PostgreSQL, and authenticated via Identity Service JWTs.
              </p>
            </div>

            {/* Architecture Overview Badges */}
            <div className="grid grid-cols-2 sm:grid-cols-3 gap-3 self-start md:self-auto shrink-0">
              <div className="flex items-center gap-2 rounded-xl border border-slate-200 bg-slate-50/70 p-3 dark:border-slate-800 dark:bg-slate-800/50">
                <Server className="h-4 w-4 text-indigo-600 dark:text-indigo-400 shrink-0" />
                <div className="text-xs">
                  <div className="font-bold text-slate-800 dark:text-slate-200">Gateway</div>
                  <div className="text-[11px] text-slate-500">YARP :5001</div>
                </div>
              </div>

              <div className="flex items-center gap-2 rounded-xl border border-slate-200 bg-slate-50/70 p-3 dark:border-slate-800 dark:bg-slate-800/50">
                <Shield className="h-4 w-4 text-purple-600 dark:text-purple-400 shrink-0" />
                <div className="text-xs">
                  <div className="font-bold text-slate-800 dark:text-slate-200">Identity</div>
                  <div className="text-[11px] text-slate-500">JWT Bearer</div>
                </div>
              </div>

              <div className="col-span-2 sm:col-span-1 flex items-center gap-2 rounded-xl border border-slate-200 bg-slate-50/70 p-3 dark:border-slate-800 dark:bg-slate-800/50">
                <Database className="h-4 w-4 text-emerald-600 dark:text-emerald-400 shrink-0" />
                <div className="text-xs">
                  <div className="font-bold text-slate-800 dark:text-slate-200">Catalog & Stock</div>
                  <div className="text-[11px] text-slate-500">Mongo + Postgres</div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      {/* Main Content Area */}
      <main className="flex-1 max-w-7xl w-full mx-auto px-4 sm:px-6 lg:px-8 py-8">
        <ProductGrid />
      </main>

      {/* Footer */}
      <footer className="border-t border-slate-200 bg-white py-6 dark:border-slate-800 dark:bg-slate-900 text-center text-xs text-slate-500 dark:text-slate-400">
        <div className="max-w-7xl mx-auto px-4 flex flex-col sm:flex-row items-center justify-between gap-2">
          <span>ShopSphere Microservices Architecture &copy; {new Date().getFullYear()}</span>
          <span className="font-mono text-[11px] text-slate-400">Next.js App Router &bull; TypeScript &bull; Tailwind CSS</span>
        </div>
      </footer>
    </div>
  );
}
