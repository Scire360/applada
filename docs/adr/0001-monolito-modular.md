# ADR-0001: Monólito modular orientado a eventos

- **Status:** aceito (2026-11-13; revisado em 2026-11-15 com a fatia de Matches completa)
- **Decisores:** responsável técnico do Applada

## Contexto

O Applada começa com uma equipe pequena e um caso de negócio crítico: confirmar vaga numa pelada sem
nunca ultrapassar a capacidade. A arquitetura v1 (`docs/architecture/arquitetura-v1.md`,
seção 1) já escolhe monólito modular com extração incremental de serviços conforme métricas, sem
microservices por entidade e sem Kubernetes no lançamento.

Precisamos de limites entre módulos verificados por teste, não só por convenção, para que uma futura
extração seja possível e para que nenhum módulo leia tabela de outro.

## Decisão

Um único processo de API (`Applada.Api`, composition root) hospeda os módulos em `backend/src/Modules/*`.

- **Um schema PostgreSQL, um `DbContext` e um conjunto de migrations por módulo.** Hoje: `identity` e
  `matches`, cada um com `__ef_migrations_history` (no schema do módulo) e tabela `outbox_messages` próprios.
- **Integração entre módulos** só por contrato publicado: consulta síncrona via projeto `*.Contracts` ou
  evento de integração versionado via outbox (ADR-0005).
- **Estrutura de projetos:** Matches é dividido em cinco projetos, todos existentes:
  - `Domain` (agregado `Match`, regras e erros);
  - `Application` (handlers, consultas, portas e métricas);
  - `Infrastructure` (`MatchesDbContext`, unidade de trabalho, migrations);
  - `Api` (endpoints Minimal API em `/api/v1/matches` e `/api/v1/me/matches`, registrados por
    `AddMatchesModule`/`MapMatchesEndpoints`);
  - `Contracts` (eventos de integração publicados: `MatchCreatedV1`, `MatchCancelledV1`, `SpotConfirmedV1` e
    `SpotReleasedV1`).

  Identity é um módulo pequeno: um projeto de implementação (pastas internas por camada) e um `Contracts`.
- **Composition root:** `Applada.Api` compõe os módulos (`Program.cs`) e hospeda o que é transversal:
  autenticação JWT, `ProblemDetails`, rate limiting das escritas e health checks. Fora do processo da API,
  `Applada.AppHost` (Aspire) e `Applada.ServiceDefaults` existem só para o ambiente local e para
  observabilidade (ADR-0007).
- **Sem mediador.** Endpoints chamam handlers de aplicação injetados (classes simples). Sem
  `GenericRepository<T>`.
- **DTOs explícitos** na API; entidades de banco e de domínio não aparecem em assinatura pública de
  endpoint.
- Módulos e workers previstos na arquitetura v1 que ainda não têm código não são criados vazios: em
  `backend/src/Modules` e `backend/src/Workers` eles são só pastas com `.gitkeep`, fora da solution.

### Regras de dependência testadas

`backend/tests/Architecture/Applada.Architecture.Tests/DependencyRulesTests.cs` (ArchUnitNET). As regras
são escritas **por namespace**, não por assembly, porque Identity é um assembly único com pastas internas;
em Matches os namespaces coincidem com os nomes dos assemblies.

| Teste | Regra |
|---|---|
| `Domain_does_not_depend_on_infrastructure_api_efcore_or_aspnet` | `*.Domain` não depende de Application, Infrastructure, Api, EF Core nem ASP.NET Core |
| `Application_depends_only_on_domain_and_contracts` | regra negativa: `*.Application` não depende de `*.Infrastructure`, `*.Api`, `Microsoft.AspNetCore*`, `Npgsql*` nem `RabbitMQ*` |
| `Modules_reference_other_modules_only_through_contracts` | um módulo só referencia outro via `*.Contracts` |
| `Contracts_do_not_depend_on_module_internals` | `*.Contracts` não depende de Domain, Application, Infrastructure nem Api |
| `No_generic_repository` | nenhum `GenericRepository<T>` |
| `Every_src_project_is_covered_by_the_architecture_rules` | todo `.csproj` novo em `src` precisa ser coberto pelas regras (impede projeto novo de escapar delas) |

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Microservices desde o início | custo operacional, consistência distribuída e observabilidade sem ganho mensurável para a escala atual; a arquitetura v1 só admite extração com métricas |
| Monólito em camadas, sem módulos | nada impediria acoplamento entre schemas; extração futura seria reescrita |
| Monólito modular com mediador | MediatR é proibido na fatia (dependência com licença comercial a evitar); sem ganho mensurável sobre handlers injetados |

## Consequências

**Positivas**
- Um deploy, uma transação por módulo e depuração simples.
- Limites verificados no CI; quebrar uma regra falha o build.
- Cada módulo pode ser extraído depois, porque só conversa por contratos e eventos.

**Negativas e limites conhecidos**
- Todos os módulos compartilham processo e banco: falha ou pico em um afeta os outros, e o PostgreSQL é
  ponto único de falha (sem réplica, failover ou estratégia de backup definidos nesta fatia).
- As regras testam dependência de tipos e referências de projeto. Elas **não** impedem um módulo de executar
  SQL contra o schema de outro; isso depende de revisão e de um `DbContext` por módulo.
- A divisão por namespace em Identity depende de disciplina de pastas dentro de um único assembly.
- A regra de Application é só uma lista de proibições: Application ainda pode referenciar EF Core e outras
  bibliotecas.
- Só existem Identity e Matches; os demais módulos da arquitetura v1 ainda não foram criados.
- A regra `Every_src_project_is_covered_by_the_architecture_rules` cobre projetos de `src`, mas a
  descoberta dos assemblies `*.Contracts` nos testes de contrato é por lista fixa: um módulo novo precisa ser
  incluído lá.
- Não há teste de carga; o comportamento sob volume real não foi medido.

## Referências

- Arquitetura: `docs/architecture/arquitetura-v1.md` §1 e §2. O desenho detalhado desta fatia vive
  fora do repositório, de propósito; as decisões que importam estão neste ADR e nos ADR-0002, 0005 e 0007.
- Contrato HTTP versionado: `docs/api/openapi-v1.json`
- Código: `backend/src/Applada.Api/Program.cs`, `backend/src/Modules/Matches/`, `backend/src/Modules/Identity/`
- Testes: `backend/tests/Architecture/Applada.Architecture.Tests/DependencyRulesTests.cs`
