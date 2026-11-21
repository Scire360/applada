# Applada

Plataforma de organização de futebol amador e rede social esportiva.

> Estado: o backend (.NET 10) tem a fundação e a primeira fatia de negócio. Existem os módulos
> Identity (usuário interno por `(issuer, subject)`), Profiles (nome de exibição) e Matches (criar partida,
> inscrição e lista de espera com promoção automática). As rotas de `/api/v1` estão no
> [contrato OpenAPI](docs/api/openapi-v1.json). A publicação usa outbox transacional com RabbitMQ,
> e o ambiente local usa Aspire com Keycloak. A web reúne as telas
> de demonstração e o fluxo local de Partidas conectadas. Os demais módulos, os consumidores de eventos e `apps/mobile`
> ainda não têm código.
> A arquitetura de referência está em `docs/` (Especificação de Arquitetura v1.0) e as decisões em
> [`docs/adr`](docs/adr/README.md).

## Estado parcial — ampliar antes da conclusão

- **Identity, Profiles, Communities, Discovery e Matches:** Identity e Profiles cobrem funções básicas; Communities
  cobre grupos, membros e convites; Discovery cobre locais (PostGIS) e a busca de peladas abertas por raio; Matches
  cobre partidas, inscrições, promoção automática da lista de espera e a chamada de presença pelo organizador.
- **Schemas e migrations:** existem para Identity, Profiles, Communities, Discovery e Matches. Os demais módulos
  ainda não possuem schemas e migrations.
- **Autorização e IDOR/BOLA:** existem regras e testes para as operações atuais desses cinco módulos. A cobertura
  ainda não alcança os módulos e endpoints que ainda não foram implementados.
- **Testes:** há testes unitários de domínio, regras de arquitetura e testes de contrato da API atual. O E2E web
  cobre fluxos existentes, mas não há app mobile nem E2E mobile; a cobertura geral ainda é incompleta.
- **Frontend conectado:** o Next.js integra partidas, presença, descoberta, grupos e perfil em fluxos específicos. As demais telas continuam
  demonstrativas e sem integração persistida.
- **Infraestrutura:** RabbitMQ está configurado e o Aspire fornece ambiente local e health checks. Redis ainda não
  está configurado como serviço da aplicação. Object storage aparece apenas no fluxo local/documentado de avatar
  via Supabase; não há configuração S3 completa para produção.
- **Observabilidade e CI:** há OpenTelemetry, métricas e health checks, além dos pipelines backend, web e CodeQL.
  Ainda faltam a operação completa da observabilidade e evidência no repositório de secret scanning.
- **Testes de carga:** existem testes de concorrência contra PostgreSQL, mas não há teste de carga ou benchmark
  da última vaga no repositório.

### Ainda pendente ou não verificável pelo repositório

- Menores de idade, consentimentos e fluxos completos de LGPD.
- Consumidores de eventos idempotentes e implementação dos módulos Teams, Social, Media, Statistics, Payments,
  Notifications e Trust.
- Retenção da falta registrada na presença (LGPD), a decidir em ADR antes do primeiro consumidor do evento.
- Backup e restauração reais, além de deploy, reverse proxy e health checks de produção.
- Validação da conexão com o projeto Supabase do ambiente. A integração está documentada, mas o repositório não
  demonstra acesso ou validação desse projeto.
- Revisão da migração de legado contra dados reais e execução com backup confirmado e plano de recuperação antes
  de produção.

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

Consulte [apps/web/README.md](apps/web/README.md) para habilitar o login de teste e conectar a agenda ao backend local.

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
