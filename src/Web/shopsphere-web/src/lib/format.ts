const currency = new Intl.NumberFormat("en-US", { style: "currency", currency: "USD" });
const dateTime = new Intl.DateTimeFormat("en-US", {
  year: "numeric",
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
});

export const formatPrice = (value: number): string => currency.format(value);

/** Backend timestamps are UTC but may lack a zone suffix; treat them as UTC. */
export function formatDate(iso: string): string {
  const normalized = /[zZ]|[+-]\d{2}:\d{2}$/.test(iso) ? iso : `${iso}Z`;
  const d = new Date(normalized);
  return Number.isNaN(d.getTime()) ? iso : dateTime.format(d);
}

export const shortOrderId = (id: string): string => id.slice(0, 8).toUpperCase();
