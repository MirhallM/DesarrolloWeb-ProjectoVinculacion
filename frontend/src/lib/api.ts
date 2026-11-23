import type { ProblemDetails } from "@/types";

/**
 * Error tipado para respuestas fallidas o problemas de red al consultar la API de .NET.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly detail?: string;

  constructor(message: string, status: number, detail?: string) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.detail = detail;
  }
}

/**
 * Normaliza la URL base del backend eliminando un posible sufijo `/api`
 * para evitar rutas duplicadas como `/api/api/products`.
 */
export function getBackendOrigin(): string {
  const raw =
    process.env.API_URL ||
    process.env.BACKEND_URL ||
    "http://localhost:5039";

  return raw.replace(/\/+$/, "").replace(/\/api$/i, "");
}

function getRequestBaseUrl(): string {
  // En el servidor (RSC / Route Handlers) se consulta directo al backend de .NET.
  if (typeof window === "undefined") {
    return `${getBackendOrigin()}/api`;
  }

  // En el navegador se consulta al mismo origen (/api/*) para que Next.js reenvíe al backend.
  return "/api";
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

async function parseErrorResponse(response: Response): Promise<ApiError> {
  const fallbackMessage = `Algo salio mal: error ${response.status}`;

  try {
    const contentType = response.headers.get("content-type") ?? "";
    if (contentType.includes("json")) {
      const body: unknown = await response.json();
      if (isRecord(body)) {
        const problem = body as ProblemDetails;
        const message =
          problem.detail || problem.title || fallbackMessage;
        return new ApiError(message, response.status, problem.detail);
      }
    }
  } catch {
    // Si el cuerpo no es JSON válido, conservamos el mensaje de estado HTTP.
  }

  return new ApiError(fallbackMessage, response.status);
}

/**
 * Cliente HTTP centralizado para peticiones hacia la API de ASP.NET Core.
 */
export async function apiFetch<T>(
  endpoint: string,
  options: RequestInit = {}
): Promise<T> {
  const baseUrl = getRequestBaseUrl().replace(/\/+$/, "");
  const cleanEndpoint = endpoint.startsWith("/") ? endpoint : `/${endpoint}`;

  let response: Response;
  try {
    response = await fetch(`${baseUrl}${cleanEndpoint}`, {
      ...options,
      headers: {
        Accept: "application/json",
        ...options.headers,
      },
    });
  } catch (error) {
    if (error instanceof DOMException && error.name === "AbortError") {
      throw error;
    }
    throw new ApiError(
      "No fue posible conectar con el servidor.",
      503
    );
  }

  if (!response.ok) {
    throw await parseErrorResponse(response);
  }

  return (await response.json()) as T;
}
