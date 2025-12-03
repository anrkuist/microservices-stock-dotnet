FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copia os csproj e restaura as dependências
COPY EventBus/EventBus.csproj EventBus/
COPY EventBusRabbitMQ/EventBusRabbitMQ.csproj EventBusRabbitMQ/
COPY Stock.API/Stock.API.csproj Stock.API/
RUN dotnet restore Stock.API/Stock.API.csproj

# Copia o resto e builda
COPY EventBus/ EventBus/
COPY EventBusRabbitMQ/ EventBusRabbitMQ/
COPY Stock.API/ Stock.API/
WORKDIR /src/Stock.API
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Stock.API.dll"]
