# ADR-0001: Monólito modular orientado a eventos

- **Status:** aceito (2026-11-13)
- **Decisores:** responsável técnico do Applada

## Contexto

O Applada começa com uma equipe pequena e um caso de negócio crítico: confirmar vaga numa pelada sem
nunca ultrapassar a capacidade (spec da fatia, §1). A arquitetura v1 (`docs/architecture/arquitetura-v1.md`,
seção 1) já escolhe monólito modular com extração incremental de serviços conforme métricas, sem
microservices por entidade e sem Kubernetes no lançamento.

Precisamos de limites entre módulos verificados por teste, não só por convenção, para que uma futura
extração seja possível e para que nenhum módulo leia tabela de outro.

## Decisão

Um único processo de API (`Applada.Api`, composition root) hospeda os módulos em `backend/src/Modules/*`.

- **Um schema PostgreSQL, um `DbContext` e um conjunto de migrations por módulo.** Hoje: `identity` e
  `matches`, cada um com `__EFMigrationsHistory` e tabela `outbox_messages` próprios.
- **Integração entre módulos** só por contrato publicado: consulta síncrona via projeto `*.Contracts` ou
  evento de integração versionado via outbox (ADR-0005).
- **Estrutura de projetos:** Matches é dividido em `Domain`, `Application`, `Infrastructure`, `Api` e
  `Contracts`. Identity é um módulo pequeno: um projeto de implementação (pastas internas por camada) e um
  `Contracts`.
- **Sem mediador.** Endpoints chamam handlers de aplicação injetados (classes simples). Sem
  `GenericRepository<T>`.
- **DTOs explícitos** na API; entidades de banco e de domínio não aparecem em assinatura pública de endpoint.
- Módulos e workers previstos na arquitetura v1 que ainda não têm código não são criados vazios.

### Regras de dependência testadas

`backend/tests/Architecture/Applada.Architecture.Tests/DependencyRulesTests.cs` (ArchUnitNET). As regras
são escritas **por namespace**, não por assembly, porque Identity é um assembly único com pastas internas;
em Matches os namespaces coincidem com os nomes dos assemblies.

| Teste | Regra |
|---|---|
| `Domain_does_not_depend_on_infrastructure_api_efcore_or_aspnet` | `*.Domain` não depende de Infrastructure, Api, EF Core nem ASP.NET Core |
| `Application_depends_only_on_domain_and_contracts` | `*.Application` depende só de Domain, Contracts e BuildingBlocks |
| `Modules_reference_other_modules_only_through_contracts` | um módulo só referencia outro via `*.Contracts` |
| `Contracts_do_not_depend_on_module_internals` | `*.Contracts` não depende de Domain, Application, Infrastructure nem Api |
| `No_generic_repository` | nenhum `GenericRepository<T>` |
| `Every_src_project_is_covered_by_the_architecture_rules` | todo `.csproj` novo em `src` precisa ser coberto pelas regras (impede projeto novo de escapar delas) |

## Alternativas consideradas

| Alternativa | Por que não |
|---|---|
| Microservices desde o início | custo operacional, consistência distribuída e observabilidade sem ganho mensurável para a escala atual; a arquitetura v1 só admite extração com métricas |
| Monólito em camadas, sem módulos | nada impediria acoplamento entre schemas; extração futura seria reescrita |
| Monólito modular com mediador | MediatR é citado na spec §2 como dependência com licença comercial a evitar; sem ganho mensurável sobre handlers injetados |

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
- Só existem Identity e Matches; os demais módulos da arquitetura v1 ainda não foram criados.
- Não há teste de carga; o comportamento sob volume real não foi medido.

## Referências

- Spec: `specs/2026-11-13-backend-fundacao-matches-design.md` §1, §3, §6, §15
- Arquitetura: `docs/architecture/arquitetura-v1.md` §1
- Código: `backend/src/Applada.Api/`, `backend/src/Modules/`
- Testes: `backend/tests/Architecture/Applada.Architecture.Tests/DependencyRulesTests.cs`
