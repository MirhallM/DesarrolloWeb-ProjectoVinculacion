import type { Metadata, Viewport } from "next";
import { Plus_Jakarta_Sans } from "next/font/google";
import { Header } from "@/components/layout/Header";
import { StoreProvider } from "@/lib/store";
import "./globals.css";

const jakarta = Plus_Jakarta_Sans({
  subsets: ["latin"],
  weight: ["400", "500", "600", "700", "800"],
  variable: "--font-jakarta",
  display: "swap",
});

export const metadata: Metadata = {
  title: {
    default: "Mini Super Nette's | Catálogo de Productos",
    template: "%s · Mini Super Nette's",
  },
  description:
    "Consulta el catálogo público de productos, precios y disponibilidad en tienda de Mini Super Nette's.",
  icons: {
    icon: "/images/nettes-logo.png",
    shortcut: "/images/nettes-logo.png",
    apple: "/images/nettes-logo.png",
  },
};

export const viewport: Viewport = {
  themeColor: "#7cb334",
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="es" className={jakarta.variable} suppressHydrationWarning>
      <body className="flex min-h-dvh flex-col bg-white font-sans text-ink antialiased">
        <StoreProvider>
          <Header />
          <div className="flex-1">{children}</div>
        </StoreProvider>
      </body>
    </html>
  );
}
