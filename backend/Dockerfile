# Imagen del backend para Railway. Contexto de compilación: la carpeta backend/ (Root Directory del servicio).
#
# Se usan las etiquetas predeterminadas de .NET 10 (Ubuntu 24.04), NO Alpine ni chiseled: la API necesita ICU para quitar acentos
# (categorización y nombre para mostrar) y esas variantes no lo traen.

# ---- Compilación ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Primero solo lo necesario para restaurar, para que Docker reutilice la capa de paquetes entre compilaciones.
COPY global.json ./
COPY src/backend/backend.csproj src/backend/
RUN dotnet restore src/backend/backend.csproj

# Solo el proyecto de la API; tests/ no entra en la imagen.
COPY src/backend/ src/backend/
RUN dotnet publish src/backend/backend.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# ---- Ejecución ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Npgsql intenta negociar GSSAPI (Kerberos) al conectar; sin esta biblioteca, cada conexión escribe
# "Cannot load library libgssapi_krb5.so.2" en el log. No se usa Kerberos, pero así el log queda limpio.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app ./

# Usuario sin privilegios que trae la imagen oficial.
USER $APP_UID

# Railway define PORT y Program.cs escucha en él. Fuera de Railway, la imagen usa su valor por defecto (8080).
EXPOSE 8080

# Sin argumentos levanta la API; "migrate", "seed" y "dp-check" ejecutan el comando y terminan.
ENTRYPOINT ["dotnet", "backend.dll"]
