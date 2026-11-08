import type { Banner, PublicProduct } from "@/types";
export { formatPrice } from "@/lib/utils";

const u = (id: string, w = 800) =>
  `https://images.unsplash.com/${id}?auto=format&fit=crop&w=${w}&q=80`;

export const CATEGORIES = [
  "Frutas y Verduras",
  "Lácteos y Huevos",
  "Panadería",
  "Bebidas",
  "Despensa",
  "Hogar y Limpieza",
  "Otros",
] as const;

/**
 * Catálogo inicial de referencia alineado con docs/business-rules.md:
 * - Solo campos públicos (id, displayName, publicPrice, category, availability, imageUrl).
 * - Sin código de Aspel (CVE_ART) ni cantidad en existencia (EXIST).
 * - Solo dos estados de disponibilidad: 'available' (Disponible) y 'out_of_stock' (Agotado).
 * - Incluye productos sin imagen (imageUrl: null) para visualizar la imagen genérica.
 */
export const PRODUCTS: PublicProduct[] = [
  {
    id: "p1",
    displayName: "Bananos Maduros 1 Lb",
    publicPrice: 12.5,
    category: "Frutas y Verduras",
    availability: "available",
    imageUrl: u("photo-1571771894821-ce9b6c11b08e"),
    isFeatured: true,
  },
  {
    id: "p2",
    displayName: "Manzanas Rojas Gala 1 Lb",
    publicPrice: 45,
    category: "Frutas y Verduras",
    availability: "available",
    imageUrl: u("photo-1560806887-1e4cd0b6cbd6"),
  },
  {
    id: "p3",
    displayName: "Aguacate Hass Unidad",
    publicPrice: 25,
    category: "Frutas y Verduras",
    availability: "available",
    imageUrl: u("photo-1519162808019-7de1683fa2ad"),
    isFeatured: true,
  },
  {
    id: "p4",
    displayName: "Tomates Frescos 1 Lb",
    publicPrice: 28,
    category: "Frutas y Verduras",
    availability: "available",
    imageUrl: null,
  },
  {
    id: "p5",
    displayName: "Canasta de Verduras de la Huerta",
    publicPrice: 350,
    category: "Frutas y Verduras",
    availability: "out_of_stock",
    imageUrl: u("photo-1590779033100-9f60a05a013d"),
  },
  {
    id: "p6",
    displayName: "Leche Entera 1 L",
    publicPrice: 38,
    category: "Lácteos y Huevos",
    availability: "available",
    imageUrl: u("photo-1639151082235-406d8eb262b9"),
  },
  {
    id: "p7",
    displayName: "Huevos de Granja Docena",
    publicPrice: 60,
    category: "Lácteos y Huevos",
    availability: "available",
    imageUrl: u("photo-1506976785307-8732e854ad03"),
    isFeatured: true,
  },
  {
    id: "p8",
    displayName: "Queso Artesanal Semiseco 400 g",
    publicPrice: 150,
    category: "Lácteos y Huevos",
    availability: "out_of_stock",
    imageUrl: null,
  },
  {
    id: "p9",
    displayName: "Pan de Masa Madre Unidad",
    publicPrice: 85,
    category: "Panadería",
    availability: "available",
    imageUrl: u("photo-1608198093002-ad4e005484ec"),
    isFeatured: true,
  },
  {
    id: "p10",
    displayName: "Pan Blanco Rebanado 680 g",
    publicPrice: 52,
    category: "Panadería",
    availability: "available",
    imageUrl: u("photo-1598373182133-52452f7691ef"),
  },
  {
    id: "p11",
    displayName: "Jugo de Naranja Natural 1 L",
    publicPrice: 75,
    category: "Bebidas",
    availability: "available",
    imageUrl: u("photo-1613478223719-2ab802602423"),
  },
  {
    id: "p12",
    displayName: "Café Hondureño en Grano 500 g",
    publicPrice: 180,
    category: "Despensa",
    availability: "available",
    imageUrl: u("photo-1524350876685-274059332603"),
  },
  {
    id: "p13",
    displayName: "Arroz Blanco Clasificado 2 Lb",
    publicPrice: 34,
    category: "Despensa",
    availability: "available",
    imageUrl: null,
  },
  {
    id: "p14",
    displayName: "Limpiador Multiusos Lavanda 750 ml",
    publicPrice: 65,
    category: "Hogar y Limpieza",
    availability: "out_of_stock",
    imageUrl: u("photo-1528740561666-dc2479dc08ab"),
  },
  {
    id: "p15",
    displayName: "Baterías Alcalinas AA Paquete 4 Unid",
    publicPrice: 95,
    category: "Otros",
    availability: "available",
    imageUrl: null,
  },
];

export const DEFAULT_BANNERS: Banner[] = [
  {
    id: "b1",
    title: "Semana fresca: hasta 30% de descuento en frutas y verduras",
    subtitle: "Productos recién cosechados, reabastecidos cada mañana.",
    cta: "Ver ofertas frescas",
    image: u("photo-1609126986933-e3c84f19d49c", 1600),
    active: true,
    createdAt: "2026-05-02",
  },
  {
    id: "b2",
    title: "Pan calientito de nuestra panadería, todos los días",
    subtitle: "Masa madre, panecillos y pan dulce horneados en tienda desde las 6 a.m.",
    cta: "Ver la panadería",
    image: u("photo-1559811814-e2c57b5e69df", 1600),
    active: true,
    createdAt: "2026-04-21",
  },
  {
    id: "b3",
    title: "La canasta del fin de semana",
    subtitle: "Todo para el almuerzo del domingo en una sola canasta.",
    cta: "Arma tu canasta",
    image: u("photo-1641893720944-15c83082f2f5", 1600),
    active: false,
    createdAt: "2026-04-08",
  },
];

export const DEFAULT_FEATURED: Record<string, boolean> = {
  p1: true,
  p3: true,
  p7: true,
  p9: true,
};

