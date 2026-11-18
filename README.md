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

## Executar a API .NET

Configure a conexão Supabase como User Secret local no formato de parâmetros do Npgsql
(`Host=...;Port=...;Database=...;Username=...;Password=...`).
O URI `postgresql://...` exibido pelo painel não deve ser copiado diretamente para o Npgsql.
Em redes IPv4, selecione `Session pooler` em Connect e use os parâmetros apresentados pelo
Supabase:

```powershell
cd backend
$secure = Read-Host "Npgsql connection string" -AsSecureString
$ptr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($ptr)
    $output = & dotnet user-secrets set "ConnectionStrings:Applada" $plain --project src\Applada.Api 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to save the connection string in User Secrets."
    }
    Write-Output "Connection string saved in User Secrets."
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($ptr)
    Remove-Variable secure, plain, ptr, output -ErrorAction SilentlyContinue
}
dotnet run --project src\Applada.Api
```

O endpoint `GET /health/ready` valida a conexão PostgreSQL; `GET /health/live` valida apenas
que a API está em execução. Não envie credenciais pelo chat, não as coloque no frontend e não
as versione. As tabelas de domínio e migrations ainda serão definidas no workshop do schema.
