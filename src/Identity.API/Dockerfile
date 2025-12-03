FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copia os csproj e restaura as dependências
COPY Identity.API/Identity.API.csproj Identity.API/
RUN dotnet restore Identity.API/Identity.API.csproj

# Copia o resto e builda
COPY Identity.API/ Identity.API/
WORKDIR /src/Identity.API
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Identity.API.dll"]
