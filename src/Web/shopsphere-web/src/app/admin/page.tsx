import type { Metadata } from "next";
import { RequireAuth } from "@/components/RequireAuth";
import { AdminConsole } from "./AdminConsole";

export const metadata: Metadata = { title: "Store management" };

export default function AdminPage() {
  return (
    <RequireAuth admin>
      <AdminConsole />
    </RequireAuth>
  );
}
