# Etapa 1: build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish -c Release -o /app/out

# Etapa 2: runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/out .

# Render inyecta PORT; la app lo lee en Program.cs
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "JuegoConcepto.dll"]
