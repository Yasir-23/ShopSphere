import { CircleCheck, CircleX, Clock, Ban } from "lucide-react";
import type { DisplayStatus } from "@/lib/orders";
import { cn } from "@/lib/utils";

const STYLES: Record<DisplayStatus, { className: string; Icon: typeof Clock }> = {
  "Order Placed": { className: "bg-emerald-50 text-emerald-700 ring-emerald-600/20", Icon: CircleCheck },
  "Payment Declined": { className: "bg-rose-50 text-rose-700 ring-rose-600/20", Icon: CircleX },
  Cancelled: { className: "bg-slate-100 text-slate-600 ring-slate-500/20", Icon: Ban },
  Processing: { className: "bg-amber-50 text-amber-700 ring-amber-600/20", Icon: Clock },
};

export function StatusBadge({ status }: { status: DisplayStatus }) {
  const { className, Icon } = STYLES[status];
  return (
    <span
      className={cn(
        "inline-flex items-center gap-1 rounded-full px-2.5 py-1 text-xs font-semibold ring-1 ring-inset",
        className
      )}
    >
      <Icon className="h-3.5 w-3.5" aria-hidden />
      {status}
    </span>
  );
}
