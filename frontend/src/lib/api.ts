import type { ProblemDetails } from "@/types";

/**
 * Error tipado para respuestas fallidas o problemas de red al consultar la API de .NET.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly detail?: string;
  readonly type?: string;
  readonly errors?: Record<string, string[]>;

  constructor(
    message: string,
    status: number,
    options?: {
      detail?: string;
      type?: string;
      errors?: Record<string, string[]>;
    }
  ) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.detail = options?.detail;
    this.type = options?.type;
    this.errors = options?.errors;
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

function firstValidationMessage(
  errors?: Record<string, string[]>
): string | undefined {
  if (!errors) return undefined;
  for (const messages of Object.values(errors)) {
    if (Array.isArray(messages) && messages.length > 0 && messages[0]) {
      return messages[0];
    }
  }
  return undefined;
}

async function parseErrorResponse(response: Response): Promise<ApiError> {
  const fallbackMessage = `Algo salió mal (error ${response.status}).`;

  try {
    const contentType = response.headers.get("content-type") ?? "";
    if (contentType.includes("json")) {
      const body: unknown = await response.json();
      if (isRecord(body)) {
        const problem = body as ProblemDetails;
        const validationMsg = firstValidationMessage(problem.errors);
        const message =
          validationMsg || problem.detail || problem.title || fallbackMessage;
        return new ApiError(message, response.status, {
          detail: problem.detail,
          type: problem.type,
          errors: problem.errors,
        });
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
      credentials: "same-origin",
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
    throw new ApiError("No fue posible conectar con el servidor.", 503);
  }

  if (!response.ok) {
    throw await parseErrorResponse(response);
  }

  if (
    response.status === 204 ||
    response.headers.get("content-length") === "0"
  ) {
    return undefined as T;
  }

  return (await response.json()) as T;
}
