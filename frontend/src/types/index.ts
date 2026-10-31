/**
 * Definiciones e interfaces TypeScript que mapean los DTOs del backend en ASP.NET Core (.NET 8).
 */

export interface ProductDto {
  id: string;
  code: string;
  name: string;
  description?: string;
  price: number;
  stock: number;
  imageUrl?: string;
  updatedAt: string;
}

export interface ApiResponse<T> {
  data: T;
  message?: string;
  success: boolean;
}

