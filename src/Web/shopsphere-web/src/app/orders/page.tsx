import type { Metadata } from "next";
import { RequireAuth } from "@/components/RequireAuth";
import { OrderHistory } from "./OrderHistory";

export const metadata: Metadata = { title: "My orders" };

export default function OrdersPage() {
  return (
    <RequireAuth>
      <OrderHistory />
    </RequireAuth>
  );
}
