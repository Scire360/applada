# ADR-0007: Identidade interna desacoplada do provedor + Keycloak só em desenvolvimento

- **Status:** aceito (2026-11-13; revisado em 2026-11-15 com o ambiente local implementado)
- **Decisores:** responsável técnico do Applada
- **Relacionado:** ADR-0004 (autenticação OIDC/OAuth2 com PKCE), que continua **pendente**.

## Contexto

O provedor de identidade de produção ainda não foi escolhido (ADR-0004 em aberto). A API precisa autenticar
desde já, e o restante do sistema não pode ficar amarrado a identificadores de um provedor específico, que
podem mudar se o provedor mudar. A spec também lista como anti-objetivo qualquer caminho de código que
autentique sem validar token, e a fatia não guarda PII: nenhum nome, e-mail ou telefone.

## Decisão

**1. Usuário interno por `(issuer, subject)`.** O módulo Identity mantém `identity.users` com `id` (UUIDv7
gerado na aplicação), `issuer`, `subject` e `created_at`, com `UNIQUE(issuer, subject)`. Os demais módulos
(Matches) conhecem apenas o `UserId` interno. Trocar de provedor passa a ser um problema de mapear novos
`(issuer, subject)` para usuários existentes, sem reescrever os módulos de negócio.

**2. Provisionamento idempotente e seguro sob concorrência.** Após autenticar, `ICurrentUser` resolve o
usuário uma vez por requisição (`UserProvisioner.EnsureUserAsync`):
`INSERT ... ON CONFLICT (issuer, subject) DO NOTHING RETURNING id`; se não retornou linha, lê o `id` já
gravado. Quem cria grava `identity.user-registered.v1` no outbox (ADR-0005) na mesma transação. A constraint
`UNIQUE` decide quem cria numa corrida.

**3. Validação padrão de JWT Bearer OIDC.** `Authority` e `Audience` vêm de configuração e são obrigatórios
(validados na subida). Não existe handler de autenticação alternativo no código da API. Nos testes de
integração, a validação do JwtBearer é reconfigurada com chave de assinatura gerada no próprio teste; o
código de produção não muda.

**4. Keycloak apenas em desenvolvimento.** O AppHost (Aspire, `backend/src/Applada.AppHost`) sobe, só para
uso local e com Docker:
- PostgreSQL/PostGIS (`postgis/postgis:17-3.5`) e RabbitMQ (`rabbitmq:4.4.0-management`);
- **Keycloak 26.8.2 como container genérico** (`quay.io/keycloak/keycloak`, `start-dev --import-realm`),
  porque a integração Aspire para Keycloak só existe em preview. O realm vem de
  `infra/keycloak/applada-dev-realm.json`, montado somente leitura como `applada-realm.json` (o import exige
  o nome `<realm>-realm.json`); sem volume, o realm volta ao estado do arquivo a cada subida;
- a API, em `ASPNETCORE_ENVIRONMENT=Development`, com `Authentication__Authority` apontando para o realm,
  `Authentication__Audience=applada-api` e `Database__ApplyMigrationsOnStartup=true`.

O client `applada-dev-scalar` é público e só aceita Authorization Code com **PKCE `S256`** (implicit, senha,
service account e device flow desligados; redirect restrito à origem da API local). Um mapper põe
`applada-api` em `aud`. Os usuários de teste (`organizador.dev`, `jogador1.dev`, `jogador2.dev` e
`jogador3.dev`) têm sufixo `.dev`, e-mails em `.invalid`, senha temporária pública e troca obrigatória no
primeiro login. A senha do admin do Keycloak é parâmetro secreto do AppHost (user-secrets), nunca arquivo
versionado. É o mesmo caminho de código de produção (JWT OIDC padrão), sem decidir o provedor de produção.

**Portas locais:** API `http://localhost:5080`, Keycloak `http://localhost:8080` (o issuer dos tokens é
`http://localhost:8080/realms/applada`, por isso a porta é fixa), dashboard do Aspire
`http://localhost:15180`. Como o redirect do realm exige a origem da API, ela também é fixa.

**Scalar e OpenAPI só em Development.** `/openapi/v1.json` e `/scalar/v1` não são registrados nem mapeados
fora de Development (lá caem no fallback autenticado: 401 anônimo, 404 autenticado; há teste). Em Development
são anônimos, porque o Scalar precisa do documento antes do login, e o Scalar já vem com o fluxo OAuth2 do
client acima. O documento é versionado em `docs/api/openapi-v1.json`.

**5. Sem PII nesta fatia.** Nada além de `issuer`, `subject` e identificadores internos é persistido.

> Estado da implementação em 2026-11-15: os cinco itens estão implementados. Os itens 1, 2, 3 e 5 têm testes
> automatizados. No item 4, os testes cobrem a superfície só de Development (`DevelopmentSurfaceTests`); a
> subida do AppHost, o realm importado e o login pelo Scalar foram verificados manualmente (fluxo Authorization
> Code + PKCE por HTTP, `GET /me`, criação e inscrição em partida), sem teste automatizado, e a interface do
> dashboard do Aspire e o clique de login no Scalar não foram exercitados.

## Alternativas consideradas

| | Abordagem | Trade-off |
|---|---|---|
| **A (escolhida)** | Keycloak como container no Aspire, só em dev; API valida JWT OIDC padrão | Mesmo caminho de código que produção; não decide o ADR-0004. Custo: container pesado e realm versionado. |
| B | Handler de autenticação por header em dev | Leve, mas cria bypass de autenticação no código; um erro de configuração vira impersonação em produção. Descartada. |
| (descartada) | Usar o `subject` do provedor como identificador em todo o sistema | amarra todos os módulos ao provedor atual e ao formato dos seus identificadores |
| (descartada) | Escolher já o provedor de produção | decisão de custo, operação e LGPD que pertence ao ADR-0004 |

## Consequências

**Positivas**
- Módulos de negócio independentes do provedor; troca de provedor localizada em Identity.
- Autenticação idêntica em dev, teste e produção, sem bypass.
- Criação do usuário sem corrida e com evento atômico.

**Negativas e limites conhecidos**
- **ADR-0004 segue em aberto:** provedor de produção, fluxo web (BFF) e mobile, e como unificar usuários se
  `issuer` mudar (migração de `(issuer, subject)`) estão a decidir.
- Toda requisição autenticada depois da primeira do usuário executa 2 consultas (o `INSERT ... ON CONFLICT`,
  que não retorna linha, e o `SELECT`) dentro de uma transação (BEGIN/COMMIT); sem cache nesta fatia
  (`UserProvisioner.cs`).
- O middleware provisiona usuário também em endpoint anônimo quando há bearer válido.
- Sem nome, e-mail ou foto: perfis ficam para o módulo Profiles. Exclusão de usuário (LGPD) não está
  desenhada; `Single()` na leitura do `id` pode quebrar se houver exclusão futura de linhas.
- Keycloak em dev é pesado e o realm é mais um artefato a manter; credenciais de teste nunca devem ser
  reaproveitadas fora de dev.
- O código do Scalar e do OpenAPI está compilado no binário de produção; só não é registrado nem mapeado fora
  de Development.
- O AppHost depende de Docker e de uma imagem de Keycloak puxada na hora; sem rede ou Docker, o ambiente
  local não sobe (os testes de integração também usam Docker, via Testcontainers).
- A revisão de segurança aprofundada de Identity (`CLAUDE.md` §7) é obrigatória antes de release e ainda
  não foi concluída para esta fatia.

## Referências

- Arquitetura: `docs/architecture/arquitetura-v1.md` (módulo Identity e ambiente de desenvolvimento). O
  desenho detalhado desta fatia vive fora do repositório, de propósito.
- Ambiente local: `infra/keycloak/README.md`, `backend/src/Applada.AppHost/AppHost.cs`
- Código: `backend/src/Modules/Identity/Applada.Modules.Identity/Infrastructure/UserProvisioner.cs`,
  `IdentityDbContext.cs`; `backend/src/Applada.Api/` (autenticação, `ICurrentUser` e
  `OpenApi/DevelopmentApiReference.cs`)
- Testes (em `backend/tests/Integration/Applada.Api.IntegrationTests/`): `IdentityTests.cs`,
  `AuthenticationTests.cs`, `DevelopmentSurfaceTests.cs`, `OpenApiContractTests.cs`
