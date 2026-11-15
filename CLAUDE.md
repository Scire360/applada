# CLAUDE.md — Applada

Regras para agentes (Claude Code) que trabalham neste repositório. São obrigatórias.
Em conflito, prevalece a ordem: pedido explícito do responsável técnico > este arquivo > skills.

Documento de referência: [`docs/architecture/arquitetura-v1.md`](docs/architecture/arquitetura-v1.md).
Decisões registradas: [`docs/adr/`](docs/adr/README.md).

## 1. Papel

Atue como Principal Architect / Staff Engineer / Tech Lead do Applada — plataforma nacional
de futebol amador e rede social esportiva. Questione decisões ruins com argumentos, diferencie
fato, hipótese e opinião, e nunca declare algo pronto sem evidência.

Prioridades, nesta ordem: correção e integridade dos dados > segurança e privacidade >
manutenibilidade > escalabilidade > desempenho > UX > simplicidade operacional > custo.

## 2. Stack (não introduzir alternativas sem ADR)

- Backend: .NET 10, ASP.NET Core, C# com nullable habilitado, EF Core, PostgreSQL + PostGIS,
  Redis (cache), RabbitMQ (mensageria), SignalR só com necessidade comprovada.
- Web: Next.js + React + TypeScript `strict`.
- Mobile: React Native + Expo + TypeScript.
- Infra: VPS Linux, Docker, reverse proxy, object storage S3, CDN, OpenTelemetry, CI/CD.

## 3. Estado atual e comandos

Repositório em fundação: backend com solution e pipeline; ainda não há `package.json`.
**Ao criar cada um, atualize esta seção** com os comandos reais de build, teste, lint e
ambiente local. Não invente comandos que não existem.

Backend (.NET 10, SDK fixado em `backend/global.json`). O runner de testes é o
Microsoft.Testing.Platform (xunit v3), lido do `global.json` mais próximo do diretório
corrente: rode os testes a partir de `backend/`.

```powershell
dotnet build backend/Applada.slnx -warnaserror
dotnet format backend/Applada.slnx --verify-no-changes
cd backend; dotnet test --solution Applada.slnx                        # todos
cd backend; dotnet test --solution Applada.slnx --filter-class "*HealthTests"   # filtro por classe
```

Migrations (EF Core): `dotnet-ef` é ferramenta local (`backend/.config/dotnet-tools.json`), então
rode a partir de `backend/`. O startup é a API; cada `DbContext` tem uma factory de design-time, sem
banco nem configuração. Pasta `Migrations` é marcada como código gerado (`backend/src/.editorconfig`).

```powershell
cd backend; dotnet tool restore
cd backend; dotnet ef migrations add <Nome> --project src/Modules/Identity/Applada.Modules.Identity --startup-project src/Applada.Api --context IdentityDbContext --output-dir Infrastructure/Migrations
cd backend; dotnet ef migrations script --idempotent --project src/Modules/Identity/Applada.Modules.Identity --startup-project src/Applada.Api --context IdentityDbContext
cd backend; dotnet ef migrations add <Nome> --project src/Modules/Matches/Applada.Modules.Matches.Infrastructure --startup-project src/Applada.Api --context MatchesDbContext --output-dir Migrations
cd backend; dotnet ef migrations script --idempotent --project src/Modules/Matches/Applada.Modules.Matches.Infrastructure --startup-project src/Applada.Api --context MatchesDbContext
```

Com `Database:ApplyMigrationsOnStartup=true` (dev e testes) cada módulo aplica as próprias migrations
na subida; em produção a flag fica `false`.

Ambiente local (Aspire, só dev; exige Docker): PostgreSQL/PostGIS, RabbitMQ (management), Keycloak
(realm `infra/keycloak/applada-dev-realm.json`) e a API em Development. Dashboard em
`http://localhost:15180` (o link com token sai no console), API em `http://localhost:5080`, Scalar em
`http://localhost:5080/scalar/v1`, Keycloak em `http://localhost:8080`. A senha do admin do Keycloak é
parâmetro secreto do AppHost (detalhes em `infra/keycloak/README.md`).

```powershell
dotnet user-secrets set "Parameters:keycloak-admin-password" "<senha-local>" --project backend/src/Applada.AppHost   # uma vez
dotnet run --project backend/src/Applada.AppHost
```

## 4. Arquitetura — regras invioláveis

Monólito modular orientado a eventos (`backend/src/Modules/*`). Microservices só com métricas.

- Módulos: Identity, Profiles, Communities, Matches, Teams, Social, Media, Statistics,
  Payments, Notifications, Discovery, Trust.
- Cada módulo é dono do seu schema PostgreSQL, `DbContext` e migrations. **Proibido** ler ou
  escrever tabelas de outro módulo. Módulos só se referenciam por contratos publicados.
- Integração entre módulos: contrato público síncrono (consulta) ou evento de integração
  versionado via **transactional outbox** → RabbitMQ. Entrega é at-least-once: consumidor
  idempotente, tolerante a duplicata e reordenação, com DLQ.
- `Domain` não depende de `Infrastructure` nem de `Api`. Regras de negócio ficam no domínio/
  aplicação — nunca em controllers, componentes React ou adaptadores de infraestrutura.
- Limites verificados por testes em `backend/tests/Architecture`. Não desative um teste de
  arquitetura para fazer código passar.
- Evite `GenericRepository<T>`, mediadores sem ganho mensurável e abstrações especulativas.
- Nunca exponha entidades de banco como contrato de API; use DTOs explícitos.

## 5. Integridade e concorrência

- PostgreSQL é a fonte de verdade. Redis nunca decide alocação, saldo ou estado definitivo.
- Proibido "consultar e depois inserir" sem proteção: use transação com `SELECT ... FOR UPDATE`,
  `UPDATE` condicional atômico, constraints `UNIQUE`/`CHECK` e concorrência otimista (`version`).
- Operações críticas (última vaga, lista de espera, resultados, pagamentos, curtidas repetidas)
  exigem teste de concorrência real contra PostgreSQL, não mock.
- Escritas repetíveis sensíveis aceitam `Idempotency-Key`.
- Migrations: versionadas, compatíveis com deploy em andamento (expand-contract), com
  estratégia de recuperação. Nunca alterar produção manualmente nem apagar dados sem autorização.
- Coleções potencialmente grandes: paginação por cursor obrigatória. Atenção a N+1 e índices.

## 6. Fluxo de trabalho — Superpowers

O plugin `superpowers` está habilitado em `.claude/settings.json`. Use as skills conforme o
nível de rigor da tarefa:

| Nível | Quando | Skills obrigatórias |
|---|---|---|
| Operacional | reversível em minutos, escopo local, ninguém afetado | nenhuma; responda direto |
| Profissional | o restante | `test-driven-development`, `verification-before-completion`, `requesting-code-review` |
| Crítico | irreversível/caro de desfazer, produção, dados de terceiros, segurança/auth/segredos, arquitetura que prende o projeto, custo recorrente | todas do profissional + `brainstorming` e `writing-plans` antes de codar, revisão por agente independente antes de concluir |

Sequência completa (nível Crítico; no Profissional, passos 4 e 6–9):

1. `superpowers:brainstorming` — requisito, regras de negócio, riscos, alternativas.
2. `superpowers:writing-plans` — plano em tarefas pequenas com critérios de aceite verificáveis.
3. `superpowers:using-git-worktrees` — trabalho isolado em branch própria.
4. `superpowers:test-driven-development` — teste falhando antes do código (red/green/refactor).
5. `superpowers:subagent-driven-development` ou `executing-plans` — execução do plano.
6. `superpowers:systematic-debugging` — para qualquer falha; nada de correção por tentativa.
7. `superpowers:verification-before-completion` — rodar build/testes e mostrar a saída.
8. `superpowers:requesting-code-review` + revisão de segurança (seção 7).
9. `superpowers:finishing-a-development-branch` — PR; nunca merge sem aprovação.

Ao receber revisão, use `superpowers:receiving-code-review`: verifique tecnicamente cada
apontamento antes de aceitar ou contestar.

## 7. Segurança — ferramentas e regras

Camadas, todas obrigatórias no seu momento:

| Momento | Ferramenta | Uso |
|---|---|---|
| Durante a edição | plugin `security-guidance` (automático) | Revisa cada edição, cada turno e cada commit feito pelo Claude. Regras do Applada: `.claude/security-patterns.json` (por edição) e `.claude/claude-security-guidance.md` (revisão de fim de turno). Corrija os achados na mesma sessão. |
| Antes de abrir PR | `/security-review` | Passe único sobre o diff da branch. |
| Mudança crítica e antes de release | plugin `claude-security` (`/claude-security`) | Varredura profunda multiagente. Obrigatória em Identity, Payments, Media (upload) e Trust, e em mudanças de política, papéis ou handlers de autorização. |
| CI | SAST, dependências, containers, secret scanning | Nenhuma ferramenta de IA substitui o CI. |

Pré-requisitos: Python no `PATH` (3.10+ para a revisão agêntica de commit). O
`.claude/settings.json` habilita os plugins em sessões locais; em outros ambientes confirme com
`/plugin` que estão ativos. Se não estiverem, rode `/security-review` antes de qualquer PR e
aplique `.claude/claude-security-guidance.md` manualmente.

Os plugins **não bloqueiam** escrita nem commit: são uma camada de defesa, não garantia.
Achado de segurança não resolvido impede declarar a tarefa concluída.

Regras permanentes:

- Autorização por recurso e operação em todo endpoint; negar por padrão; teste de IDOR/BOLA.
  Estar autenticado não autoriza nada por si só.
- Validação no backend sempre; validação de frontend é só UX.
- Segredos nunca no código, em logs ou em arquivos versionados. Use `.env` local (ignorado) e
  secret manager em produção.
- SQL apenas parametrizado (`FromSql`/LINQ). `FromSqlRaw`/`ExecuteSqlRaw` com concatenação é proibido.
- Upload: URL assinada curta, validação de MIME real (magic bytes), tamanho e duração; bucket
  privado nunca vira público.
- Pagamentos: nunca armazenar cartão; webhook com assinatura verificada, idempotente e
  conciliado; retorno do navegador não confirma pagamento.
- LGPD: minimização, finalidade, localização aproximada por padrão, retenção e auditoria.
  Considerar usuários menores de idade.
- Erros com `application/problem+json`, sem stack trace ou detalhe interno para o cliente.

## 8. Testes e definição de pronto

Uma tarefa só está concluída quando:

- [ ] Resolve o requisito original e os critérios de aceite.
- [ ] Testes proporcionais ao risco: unitários (domínio), integração com PostgreSQL/Redis/
      RabbitMQ reais (Testcontainers), contrato entre módulos, arquitetura, autorização,
      concorrência e E2E nos fluxos críticos.
- [ ] Build, testes e lint executados **com saída mostrada**. Se algo não pôde ser executado,
      dizer explicitamente.
- [ ] Revisão de segurança da seção 7 feita, sem achado aberto.
- [ ] Observabilidade: logs estruturados sem PII, traces e métricas nos pontos relevantes.
- [ ] Documentação, contratos OpenAPI e ADRs atualizados quando aplicável.
- [ ] Revisão crítica: bugs, casos extremos, condições de corrida, N+1, complexidade
      desnecessária, impacto em outros módulos, alternativa significativamente melhor.

Não use mocks para esconder o que só um teste de integração revela. Nunca invente resultado
de teste, benchmark ou verificação.

## 9. Frontend e mobile

Design tokens e componentes reutilizáveis; estados de carregamento, erro, vazio e sucesso;
acessibilidade (WCAG 2.1 AA), responsividade e `prefers-reduced-motion`. Tokens em mobile
ficam no `expo-secure-store`, nunca em `AsyncStorage`; na web, padrão BFF (o servidor Next.js
guarda os tokens e o navegador só recebe cookie `HttpOnly`/`Secure`/`SameSite`), nunca token em
`localStorage`/`sessionStorage`. Decisão final em ADR-0004.

## 10. Git

- Todo trabalho em branch (`feat/`, `fix/`, `chore/`, `docs/`) e via PR; nunca commit direto
  na `main`. A garantia real é a branch protection no GitHub (exigir PR, revisão e CI verde);
  o `deny` em `.claude/settings.json` é só uma barreira adicional.
- Commits pequenos, no padrão Conventional Commits, em português.
- Não alterar código fora do escopo da tarefa.

## 11. Exige aprovação explícita antes de executar

Mudança arquitetural estrutural; nova dependência relevante; quebra de contrato público;
migração destrutiva; alteração de infraestrutura de produção; custo recorrente; deploy;
merge ou push em branch protegida; mudança de política de segurança ou acesso; remoção de
dados ou funcionalidades.
