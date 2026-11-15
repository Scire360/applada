# Applada

Plataforma de organização de futebol amador e rede social esportiva.

> Estado: o backend (.NET 10) tem a fundação e a primeira fatia de negócio. Existem os módulos
> Identity (usuário interno por `(issuer, subject)`) e Matches (criar partida, inscrição, lista de espera
> com promoção automática e as 9 rotas de `/api/v1` descritas em [`docs/api/openapi-v1.json`](docs/api/openapi-v1.json)),
> outbox transacional com RabbitMQ e o ambiente local com Aspire e Keycloak. A web reúne as telas
> de demonstração do produto. Os demais módulos, os consumidores de eventos e `apps/mobile`
> ainda não têm código.
> A arquitetura de referência está em `docs/` (Especificação de Arquitetura v1.0) e as decisões em
> [`docs/adr`](docs/adr/README.md).

## Executar o backend

Pré-requisitos: SDK .NET indicado em `backend/global.json` e Docker em execução (o ambiente local e os
testes de integração sobem PostgreSQL/PostGIS e RabbitMQ em containers).

```powershell
# build, formatação e testes (os testes rodam a partir de backend/)
dotnet build backend/Applada.slnx -warnaserror
dotnet format backend/Applada.slnx --verify-no-changes
cd backend; dotnet test --solution Applada.slnx; cd ..

# ambiente local: PostgreSQL, RabbitMQ, Keycloak e a API (uma vez: senha do admin do Keycloak)
dotnet user-secrets set "Parameters:keycloak-admin-password" "<senha-local>" --project backend/src/Applada.AppHost
dotnet run --project backend/src/Applada.AppHost
```

Com o AppHost no ar: API em `http://localhost:5080` (`/health/live`, `/health/ready`), Scalar em
`http://localhost:5080/scalar/v1` (login pelo Keycloak com os usuários de teste de
[`infra/keycloak`](infra/keycloak/README.md)) e dashboard do Aspire em `http://localhost:15180`. OpenAPI,
Scalar e Keycloak existem só em desenvolvimento. Comandos de migrations, filtros de teste e a
regeneração do contrato OpenAPI estão em [`CLAUDE.md`](CLAUDE.md) (seção 3).

## Executar o frontend web

```powershell
cd apps\web
npm ci
npm run dev
```

Consulte [apps/web/README.md](apps/web/README.md) para os fluxos disponíveis e as verificações.

## Estrutura

```text
apps/
  web/            Next.js + TypeScript estrito
  mobile/         React Native + Expo
backend/          .NET 10 — monólito modular orientado a eventos
  src/
    Applada.Api/              composition root e endpoints HTTP
    Applada.AppHost/          Aspire (somente desenvolvimento)
    Applada.ServiceDefaults/  OpenTelemetry, health checks, resiliência
    BuildingBlocks/           contratos, persistência e mensageria compartilhados
    Modules/                  um diretório por domínio (ownership exclusivo de schema)
    Workers/                  processos assíncronos (mídia, notificações, projeções)
  tests/          Unit, Architecture, Integration, Contract, EndToEnd
infra/            compose, reverse proxy, observabilidade, migrations, deploy
docs/             ADRs, contratos de API, threat model, runbooks
```

## Módulos

| Módulo | Responsabilidade |
|---|---|
| Identity | autenticação, sessões e credenciais |
| Profiles | perfis, seguidores e bloqueios |
| Communities | grupos, membros e convites |
| Matches | peladas, vagas, lista de espera e presença |
| Teams | equipes e campeonatos |
| Social | publicações, comentários e feed |
| Media | upload e processamento de imagens e vídeos |
| Statistics | resultados, estatísticas e rankings |
| Payments | rateios e pagamentos |
| Notifications | push, inbox e entregas |
| Discovery | busca geográfica e descoberta |
| Trust | denúncias, moderação e auditoria (Trust & Safety) |

## Regras de dependência

- `Domain` não depende de `Infrastructure` nem de `Api`.
- Módulos só se referenciam por contratos publicados; nunca por tabelas ou repositórios de outro módulo.
- Cada módulo tem seu `DbContext`, schema PostgreSQL e migrations próprios.
- As regras são verificadas automaticamente em `backend/tests/Architecture`.

Decisões arquiteturais ficam registradas em [`docs/adr`](docs/adr/README.md).
