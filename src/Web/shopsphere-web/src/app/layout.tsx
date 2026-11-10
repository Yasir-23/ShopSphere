import type { Metadata } from "next";
import { Suspense } from "react";
import "./globals.css";
import { AuthProvider } from "@/context/AuthContext";
import { CartProvider } from "@/context/CartContext";
import { Navbar, NavbarFallback } from "@/components/Navbar";
import { CartDrawer } from "@/components/CartDrawer";

export const metadata: Metadata = {
  title: {
    default: "ShopSphere | Tech, audio & smart home gear",
    template: "%s | ShopSphere",
  },
  description: "Shop electronics, peripherals and smart home gear with a fast, secure checkout.",
};

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" className="h-full antialiased">
      <body className="flex min-h-full flex-col bg-slate-50 text-slate-900">
        <AuthProvider>
          <CartProvider>
            <Suspense fallback={<NavbarFallback />}>
              <Navbar />
            </Suspense>
            <main className="flex-1">{children}</main>
            <footer className="border-t border-slate-200 bg-white">
              <div className="mx-auto flex max-w-7xl flex-col items-center justify-between gap-2 px-4 py-6 text-sm text-slate-500 sm:flex-row sm:px-6 lg:px-8">
                <p>© {new Date().getFullYear()} ShopSphere. All rights reserved.</p>
                <p>Secure checkout · Saved carts · Order tracking</p>
              </div>
            </footer>
            <CartDrawer />
          </CartProvider>
        </AuthProvider>
      </body>
    </html>
  );
}
