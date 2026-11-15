# ADR-0002: PostgreSQL + PostGIS como fonte de verdade

- **Status:** aceito (2026-11-13)
- **Decisores:** responsável técnico do Applada

## Contexto

O requisito central da primeira fatia é nunca confirmar mais jogadores do que a capacidade da partida e
nunca promover alguém da lista de espera duas vezes, com muitas requisições simultâneas na mesma partida
(spec §1, §2). A arquitetura v1 define o banco relacional como fonte de verdade; Redis nunca decide estado
definitivo (`CLAUDE.md` §5). O produto terá descoberta por raio (módulo Discovery, fora desta fatia), o que
justifica PostGIS desde já.

## Decisão

- **PostgreSQL com PostGIS** como único armazenamento de estado definitivo. Os testes de integração usam a
  imagem `postgis/postgis:17-3.5` (`ApiFactory`). Nesta fatia nenhum recurso geoespacial é usado; PostGIS
  fica disponível para Discovery.
- **Acesso via EF Core + Npgsql**, um schema e um `DbContext` por módulo (ADR-0001), nomes em snake_case,
  IDs UUIDv7 gerados na aplicação (`Guid.CreateVersion7()`) e timestamps `timestamptz` em UTC.
- **Concorrência da última vaga (spec §4.3, alternativa A):** cada escrita sobre inscrições, capacidade ou
  status roda numa transação `READ COMMITTED` que:
  1. define o timeout do lock com `SELECT set_config('lock_timeout', <valor>, true)`, equivalente
     parametrizado de `SET LOCAL` (que não aceita parâmetro);
  2. trava a linha da partida com `SELECT * FROM matches.matches WHERE id = {id} FOR UPDATE`, via `FromSql`
     parametrizado;
  3. aplica a regra no agregado `Match`, que altera contadores e inscrições;
  4. grava partida, inscrições, chave de idempotência e outbox num único `SaveChanges` e faz commit.
  Só uma linha é travada por transação, então não há deadlock entre partidas. Estouro de `lock_timeout`
  (SQLSTATE `55P03`) vira o erro `lock-timeout`, que a API devolve como 503.
- **`CHECK` como defesa em profundidade.** As regras estão no domínio e o banco as repete:
  `capacity` entre 1 e 200, `duration_minutes` entre 15 e 600, `confirmed_count` entre 0 e `capacity`,
  `waitlist_count` entre 0 e 50, `status` e `visibility` em conjuntos fechados, e `ck_registrations_status`.
  Um bug de aplicação que tentasse lotar a partida aborta a transação em vez de gravar estado inválido.
  `UNIQUE(match_id, user_id)` em `registrations` e `PRIMARY KEY(user_id, key)` em `idempotency_keys`
  completam as garantias.
- **Ordem FIFO da fila** vem de `queue_seq`, incrementado a partir de `matches.next_queue_seq` sob o lock;
  não de `now()`, que é o início da transação e poderia inverter a ordem.
- **Concorrência otimista (`version`)** só para edições do organizador (`PATCH`, cancelamento). `xmin` não
  é usado porque inscrições alteram contadores na mesma linha e fariam o organizador receber 412 a cada
  inscrição.
- **Sem retry automático do Npgsql/EF Core.** `EnableRetryOnFailure` não é usado: a estratégia de retry não
  convive com a transação explícita da unidade de trabalho (`MatchesUnitOfWork`). Falha transitória de banco
  chega ao cliente como erro; o cliente repete (escritas sensíveis aceitam `Idempotency-Key`).

## Alternativas consideradas

| Abordagem | Trade-off | Decisão |
|---|---|---|
| **A: `SELECT ... FOR UPDATE` na linha da partida + `confirmed_count` com `CHECK`** | serializa operações por partida (disputa pequena, ~20 a 50 pessoas); promoção em vários passos é simples de raciocinar; `CHECK` protege o banco | escolhida |
| B: `UPDATE` condicional atômico do contador, sem lock | ótimo para incrementar, mas a promoção da espera (escolher o próximo, mudar status) tem mais casos de corrida | descartada |
| C: isolamento `SERIALIZABLE` com retry | correto, porém com retries imprevisíveis sob disputa e mais difícil de testar | descartada |
| Redis como árbitro de vagas | sem as constraints transacionais do banco; contraria `CLAUDE.md` §5 | descartada |

## Consequências

**Positivas**
- Invariantes críticas valem no banco, independentemente de bug de aplicação.
- Raciocínio simples sobre a promoção da espera: tudo acontece sob um lock, na mesma transação.
- O mesmo banco atende a consulta relacional e, depois, a geoespacial.

**Negativas e limites conhecidos**
- Escritas na mesma partida são serializadas; uma transação lenta segura as demais até o `lock_timeout`, e
  quem estoura recebe 503. Dimensionado para dezenas de pessoas por partida; não há teste de carga que meça
  o limite.
- O banco é ponto único de falha: sem retry automático e sem réplica definida, banco fora do ar significa
  API sem escrita. Backup, alta disponibilidade e RPO/RTO: a decidir.
- A corrida de idempotência com a mesma chave pode passar do `lock_timeout` e virar 503.
- Os `CHECK` duplicam regras do domínio; mudar um limite exige migration e mudança de domínio (a spec prevê
  migration expansiva para ampliar `visibility`).
- PostGIS está previsto mas sem uso; o benefício é futuro.

## Referências

- Spec: `specs/2026-11-13-backend-fundacao-matches-design.md` §4.3, §8, §9
- Código: `backend/src/Modules/Matches/Applada.Modules.Matches.Infrastructure/MatchesUnitOfWork.cs`,
  `MatchesSession.cs`, `MatchesDbContext.cs`, `Configurations/`
- Testes: `backend/tests/Integration/Applada.Api.IntegrationTests/MatchesPersistenceTests.cs`,
  `MatchesLockTimeoutApiTests.cs`, `MatchesLifecycleTests.cs`
