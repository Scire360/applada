# ADR-0007: Identidade interna desacoplada do provedor + Keycloak só em desenvolvimento

- **Status:** aceito (2026-11-13)
- **Decisores:** responsável técnico do Applada
- **Relacionado:** ADR-0004 (autenticação OIDC/OAuth2 com PKCE), que continua **pendente**.

## Contexto

O provedor de identidade de produção ainda não foi escolhido (ADR-0004 em aberto). A API precisa autenticar
desde já, e o restante do sistema não pode ficar amarrado a identificadores de um provedor específico, que
podem mudar se o provedor mudar. A spec também lista como anti-objetivo qualquer caminho de código que
autentique sem validar token (§2), e a fatia não guarda PII: nenhum nome, e-mail ou telefone.

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

**4. Keycloak apenas em desenvolvimento.** O AppHost (Aspire) sobe Keycloak como container com realm
versionado (`infra/keycloak/applada-dev-realm.json`), client público com PKCE e usuários de teste, marcados
como de teste. É o mesmo caminho de código de produção (JWT OIDC padrão), sem decidir o provedor de produção.
Scalar fica mapeado só em Development para teste manual com Authorization Code + PKCE.

**5. Sem PII nesta fatia.** Nada além de `issuer`, `subject` e identificadores internos é persistido.

> Estado da implementação em 2026-11-14: itens 1, 2, 3 e 5 estão implementados e testados. O AppHost com
> Keycloak e o Scalar (item 4) ainda não foram implementados (`backend/src/Applada.AppHost` está vazio); a
> decisão está registrada aqui e a entrega segue o plano da fatia.

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
- A revisão de segurança aprofundada de Identity (`CLAUDE.md` §7) é obrigatória antes de release e ainda
  não foi concluída para esta fatia.

## Referências

- Spec: `specs/2026-11-13-backend-fundacao-matches-design.md` §2, §4.2, §11
- Código: `backend/src/Modules/Identity/Applada.Modules.Identity/Infrastructure/UserProvisioner.cs`,
  `IdentityDbContext.cs`; `backend/src/Applada.Api/` (autenticação e `ICurrentUser`)
- Testes: `backend/tests/Integration/Applada.Api.IntegrationTests/IdentityTests.cs`, `AuthenticationTests.cs`
