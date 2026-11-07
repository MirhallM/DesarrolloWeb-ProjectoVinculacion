/**
 * Utilidades generales de formateo (moneda en Lempiras, fechas).
 */

/** Formatea precio en Lempiras hondureños, ej. `L 1,500.00` (ISV incluido según reglas de negocio). */
export function formatPrice(amount: number): string {
  return `L ${amount.toLocaleString("en-US", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  })}`;
}

export function formatCurrency(amount: number, currency = "HNL", locale = "es-HN"): string {
  if (currency === "HNL") {
    return formatPrice(amount);
  }
  return new Intl.NumberFormat(locale, {
    style: "currency",
    currency,
  }).format(amount);
}

export function formatDate(date: string | Date, locale = "es-HN"): string {
  const parsedDate = typeof date === "string" ? new Date(date) : date;
  return new Intl.DateTimeFormat(locale, {
    dateStyle: "medium",
    timeStyle: "short",
  }).format(parsedDate);
}

