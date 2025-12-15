# E-commerce Microservices

Sistema de e-commerce com arquitetura de microsserviços usando .NET 9, RabbitMQ e SQL Server.

## Arquitetura

```
┌─────────────────┐
│   API Gateway   │ ← YARP Reverse Proxy
│   (porta 5050)  │
└────────┬────────┘
         │
    ┌────┴────┬──────────────┐
    ▼         ▼              ▼
┌───────┐ ┌───────┐    ┌──────────┐
│ Sales │ │ Stock │    │ Identity │
│ :5154 │ │ :5216 │    │  :5171   │
└───┬───┘ └───┬───┘    └──────────┘
    │         │
    │   HTTP  │ (validação síncrona)
    └────────►│
              │
    ┌─────────┴─────────┐
    │     RabbitMQ      │ (atualização assíncrona)
    │    :5672/:15672   │
    └───────────────────┘
```

## Estrutura do Projeto

```
src/
├── ApiGateway/          # YARP reverse proxy
│   └── Dockerfile
├── Identity.API/        # Autenticação e usuários
│   └── Dockerfile
├── Sales.API/           # Pedidos
│   └── Dockerfile
├── Stock.API/           # Produtos e estoque
│   └── Dockerfile
├── EventBus/            # Abstrações de eventos
├── EventBusRabbitMQ/    # Implementação RabbitMQ
└── docker-compose.yml   # Todos os serviços
```

## Variáveis de Ambiente

| Variável                   | Padrão                                            | Descrição         |
| -------------------------- | ------------------------------------------------- | ----------------- |
| `Jwt__SecretKey`           | `secret-key-poc-12345-ecommerce-microservices`    | Chave do JWT      |
| `RabbitMQ__Host`           | `localhost` (local) / `rabbitmq` (docker)         | Host do RabbitMQ  |
| `ConnectionStrings__Redis` | `localhost:6379` / `redis:6379`                   | Redis pro SignalR |
| `StockService__BaseUrl`    | `http://localhost:5216` / `http://stock-api:8080` | URL do Stock.API  |
| `Seq__Url`                 | `http://localhost:5341` / `http://seq:80`         | URL do Seq (logs) |

## Pré-requisitos

- [Docker](https://www.docker.com/) e Docker Compose
- [.NET 9 SDK](https://dotnet.microsoft.com/download) (só se for rodar sem Docker)

## Como rodar

### Opção 1: Docker Compose (Recomendado)

Sobe tudo de uma vez: SQL Server, RabbitMQ, Redis e todas as APIs.

```bash
docker-compose up --build
```

Pronto! Acessa http://localhost:5050

### Opção 2: Rodar localmente

#### 1. Subir infraestrutura

```bash
docker-compose up -d sqlserver rabbitmq redis
```

#### 2. Rodar cada serviço

Abre 4 terminais:

```bash
# Terminal 1 - Identity API
cd Identity.API && dotnet run

# Terminal 2 - Stock API
cd Stock.API && dotnet run

# Terminal 3 - Sales API
cd Sales.API && dotnet run

# Terminal 4 - API Gateway
cd ApiGateway && dotnet run
```

### 3. Testar

Acessa o Swagger de cada serviço:

- Identity: http://localhost:5171/swagger
- Sales: http://localhost:5154/swagger
- Stock: http://localhost:5216/swagger

Ou usa o Gateway (recomendado):

- http://localhost:5050/

## Serviços do Docker Compose

| Serviço        | Porta       | Descrição                                |
| -------------- | ----------- | ---------------------------------------- |
| `api-gateway`  | 5050        | YARP Reverse Proxy                       |
| `identity-api` | 5171        | Autenticação e usuários                  |
| `sales-api`    | 5154        | Pedidos                                  |
| `stock-api`    | 5216        | Produtos e estoque                       |
| `sqlserver`    | 1433        | SQL Server 2022                          |
| `rabbitmq`     | 5672, 15672 | Message Broker (UI: guest/guest)         |
| `redis`        | 6379        | Cache pro SignalR                        |
| `seq`          | 5341        | Visualização de logs (admin / Admin123!) |

## Autenticação

Todos os endpoints (exceto login/register e GET de produtos) precisam de token JWT.

### Criar usuário

```bash
curl -X POST http://localhost:5050/auth/auth/register \
  -H "Content-Type: application/json" \
  -d '{"username": "joao", "email": "joao@email.com", "password": "123456"}'
```

### Fazer login

```bash
curl -X POST http://localhost:5050/auth/auth/login \
  -H "Content-Type: application/json" \
  -d '{"username": "joao", "password": "123456"}'
```

Resposta:

```json
{
  "token": "eyJhbGciOiJIUzI1NiIs...",
  "username": "joao",
  "email": "joao@email.com",
  "role": "Customer"
}
```

### Usar o token

Passa o token no header `Authorization`:

```bash
curl http://localhost:5050/sales/orders \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIs..."
```

## Fluxo de Pedido

1. **Cliente** faz POST `/sales/orders` com productId, quantity e price
2. **Sales.API** chama **Stock.API** via HTTP pra validar estoque
3. Se tiver estoque, cria o pedido como "Confirmed"
4. Publica evento **OrderCreatedEvent** no RabbitMQ
5. **Stock.API** recebe o evento e baixa o estoque

```
Cliente → Sales.API → Stock.API (HTTP: valida)
                   ↓
              RabbitMQ
                   ↓
              Stock.API (baixa estoque)
```

## Rotas do Gateway

| Rota          | Destino                       |
| ------------- | ----------------------------- |
| `/sales/*`    | Sales.API (localhost:5154)    |
| `/stock/*`    | Stock.API (localhost:5216)    |
| `/auth/*`     | Identity.API (localhost:5171) |
| `/identity/*` | Identity.API (localhost:5171) |

### Exemplos

```bash
# Listar produtos (público)
curl http://localhost:5050/stock/products

# Criar pedido (precisa de token)
curl -X POST http://localhost:5050/sales/orders \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"productId": 1, "quantity": 2, "price": 99.90}'

# Ver meus pedidos
curl http://localhost:5050/sales/orders \
  -H "Authorization: Bearer $TOKEN"
```

## Bancos de Dados

Cada serviço tem seu próprio banco (Database-per-Service):

- **IdentityDB** - usuários
- **SalesDB** - pedidos
- **StockDB** - produtos

As migrations rodam automaticamente quando o serviço inicia.

### Connection Strings

**Docker:** Já configurado nas variáveis de ambiente do docker-compose.

**Local:** Muda no `appsettings.json` de cada serviço:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=SalesDB;User Id=sa;Password=Your_password123;TrustServerCertificate=True"
  }
}
```

## SignalR (Tempo Real)

O Sales.API tem um hub SignalR pra notificações em tempo real.

### Conectar

```javascript
const connection = new signalR.HubConnectionBuilder()
  .withUrl("http://localhost:5154/hubs/orders?access_token=" + token)
  .build();

await connection.start();

// Inscrever pra receber updates de um pedido
await connection.invoke("SubscribeToOrder", orderId);

// Receber notificações
connection.on("OrderStatusChanged", (data) => {
  console.log("Pedido atualizado:", data);
});
```

## RabbitMQ

Acessa o painel de gerenciamento em http://localhost:15672

- **Usuário:** guest
- **Senha:** guest

Lá dá pra ver as filas criadas (OrderCreatedEvent, etc) e as mensagens passando.

## Monitoramento e Logs

O sistema usa **Serilog** com **Seq** pra centralizar os logs de todos os microsserviços.

### Dashboard de Logs

Acessa http://localhost:5341 pra abrir o Seq (interface web de logs).

- **Usuário:** admin
- **Senha:** Admin123!

### O que é logado?

- Requests HTTP
- Erros e exceções
- Eventos de negócio (pedidos criados, estoque baixado, etc)
- Falhas de validação

### Filtrar por serviço

No Seq, usa o filtro:

```
Service = "Sales.API"
Service = "Stock.API"
Service = "Identity.API"
```

### Logs no Console

Se rodar local, cada serviço mostra logs no terminal no formato:

```
[14:32:15 INF] [Sales.API] Pedido 123 criado com sucesso
[14:32:15 ERR] [Stock.API] Falha ao validar estoque: produto não encontrado
```

### Níveis de Log

| Nível | O que significa                      |
| ----- | ------------------------------------ |
| `DBG` | Debug (só em desenvolvimento)        |
| `INF` | Informação (fluxo normal)            |
| `WRN` | Aviso (algo estranho, mas funcionou) |
| `ERR` | Erro (algo deu errado)               |
| `FTL` | Fatal (serviço vai morrer)           |

### Configuração

Cada serviço já vem configurado com Serilog no `Program.cs`. Pra mudar o nível mínimo:

```csharp
.MinimumLevel.Debug()  // Ver tudo (muito verbose)
.MinimumLevel.Information()  // Padrão
.MinimumLevel.Warning()  // Só problemas
```

## Segurança

- **BCrypt**: Senhas são hasheadas com BCrypt.Net
- **JWT**: Tokens expiram em 8 horas
- **Roles**: `Customer` (padrão) e `Admin` (gerencia produtos)

## Testes

O projeto inclui testes unitários usando **xUnit**, **Moq** e **FluentAssertions**.

### Rodar os testes

```bash
dotnet test
```

### Estrutura de testes

```
Stock.API.Tests/
├── Controllers/
│   └── ProductsControllerTests.cs  # CRUD de produtos, validação de estoque
└── Models/
    └── ProductTests.cs              # Testes do modelo Product

Sales.API.Tests/
├── Controllers/
│   └── OrdersControllerTests.cs    # Criação e cancelamento de pedidos
├── Models/
│   └── OrderTests.cs               # Testes do modelo Order, TotalPrice
└── Services/
    └── StockServiceTests.cs        # Comunicação HTTP com Stock.API
```

### Cobertura

- **Stock.API**: Produtos (CRUD), validação de estoque
- **Sales.API**: Pedidos (criar, cancelar, listar), estatísticas, comunicação com Stock

## Rate Limiting

O API Gateway implementa rate limiting pra proteger contra abuso e DDoS.

### Políticas configuradas

| Política | Limite            | Aplicada em        |
| -------- | ----------------- | ------------------ |
| Global   | 100 req/minuto    | Todas as rotas     |
| `auth`   | 10 req/5 minutos  | Login e registro   |
| `orders` | 20 req/10 minutos | Criação de pedidos |

### Resposta quando limite atingido

```json
{
  "error": "Muitas requisições! Calma aí...",
  "message": "Limite de requisições atingido. Tente novamente em 60 segundos.",
  "retryAfterSeconds": 60
}
```

**HTTP Status**: `429 Too Many Requests`  
**Header**: `Retry-After: 60`

### Customizar limites

Edita o `appsettings.json` do ApiGateway:

```json
{
  "RateLimiting": {
    "Global": { "PermitLimit": 100, "WindowMinutes": 1 },
    "Auth": { "PermitLimit": 10, "WindowMinutes": 5 },
    "Orders": { "PermitLimit": 20, "WindowMinutes": 10 }
  }
}
```

## Health Checks

Cada serviço expõe endpoints de health check pra monitoramento.

### Endpoints

| Endpoint        | Descrição                                       |
| --------------- | ----------------------------------------------- |
| `/health`       | Status completo (todas as dependências)         |
| `/health/ready` | Readiness - serviço pronto pra receber tráfego  |
| `/health/live`  | Liveness - serviço está vivo (sem dependências) |

### O que é verificado

| Serviço        | Dependências verificadas           |
| -------------- | ---------------------------------- |
| `Identity.API` | SQL Server                         |
| `Sales.API`    | SQL Server, RabbitMQ, Redis        |
| `Stock.API`    | SQL Server, RabbitMQ               |
| `ApiGateway`   | Identity.API, Sales.API, Stock.API |

### Exemplo de resposta

```bash
curl http://localhost:5050/health
```

```json
{
  "status": "Healthy",
  "totalDuration": "00:00:00.1234567",
  "entries": {
    "identity-api": {
      "status": "Healthy",
      "duration": "00:00:00.0234567"
    },
    "sales-api": {
      "status": "Healthy",
      "duration": "00:00:00.0345678"
    },
    "stock-api": {
      "status": "Healthy",
      "duration": "00:00:00.0456789"
    }
  }
}
```

### Status possíveis

| Status      | Significado                                          |
| ----------- | ---------------------------------------------------- |
| `Healthy`   | Tudo funcionando normalmente                         |
| `Degraded`  | Funcionando, mas com alguma dependência com problema |
| `Unhealthy` | Serviço não consegue operar corretamente             |

### Uso com Kubernetes/Docker

Os endpoints são usados pra:

- **Liveness Probe** (`/health/live`): Reinicia container se falhar
- **Readiness Probe** (`/health/ready`): Remove do load balancer se não estiver pronto

```yaml
# docker-compose healthcheck example
healthcheck:
  test: ["CMD", "curl", "-f", "http://localhost:8080/health/live"]
  interval: 30s
  timeout: 10s
  retries: 3
```
