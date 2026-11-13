# Applada

Plataforma de organização de futebol amador e rede social esportiva.

> Estado: o frontend Next.js em `apps/web` cobre as 43 vistas identificadas no sistema
> anterior. As páginas usam dados demonstrativos e ainda não estão conectadas à API.
> A arquitetura de referência está em `docs/` (Especificação de Arquitetura v1.0).

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

## Executar o frontend web

```powershell
cd apps\web
npm install
npm run dev
```

O frontend usa Next.js, React e TypeScript estrito. Consulte [`apps/web/README.md`](apps/web/README.md)
para comandos de build, lint e verificação de tipos.
