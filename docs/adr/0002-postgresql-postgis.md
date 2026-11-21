# ADR-0002: PostgreSQL + PostGIS como fonte de verdade

- **Status:** aceito (2026-11-13; revisado em 2026-11-15 com a fatia de Matches completa)
- **Decisores:** responsável técnico do Applada

## Contexto

O requisito central da primeira fatia é nunca confirmar mais jogadores do que a capacidade da partida e
nunca promover alguém da lista de espera duas vezes, com muitas requisições simultâneas na mesma partida.
A arquitetura v1 define o banco relacional como fonte de verdade; Redis nunca decide estado
definitivo (`CLAUDE.md` §5). O produto terá descoberta por raio (módulo Discovery, fora desta fatia), o que
justifica PostGIS desde já.

## Decisão

- **PostgreSQL com PostGIS** como único armazenamento de estado definitivo. Os testes de integração usam a
  imagem `postgis/postgis:17-3.5` (`ApiFactory`). Nesta fatia nenhum recurso geoespacial é usado; PostGIS
  fica disponível para Discovery.
- **Acesso via EF Core + Npgsql**, um schema e um `DbContext` por módulo (ADR-0001), nomes em snake_case,
  IDs UUIDv7 gerados na aplicação (`Guid.CreateVersion7()`) e timestamps `timestamptz` em UTC.
- **Concorrência da última vaga (alternativa A):** cada escrita sobre inscrições, capacidade ou
  status roda numa transação `READ COMMITTED` que:
  1. define o timeout do lock com `SELECT set_config('lock_timeout', <valor>, true)`, equivalente
     parametrizado de `SET LOCAL` (que não aceita parâmetro);
  2. trava a linha da partida com `SELECT * FROM matches.matches WHERE id = {id} FOR UPDATE`, via `FromSql`
     parametrizado;
  3. aplica a regra no agregado `Match`, que altera contadores e inscrições;
  4. grava partida, inscrições, chave de idempotência e outbox num único `SaveChanges` e faz commit.
  Só uma linha é travada por transação, então não há deadlock entre partidas. Estouro de `lock_timeout`
  (SQLSTATE `55P03`) vira o erro de domínio `MatchErrors.LockTimeout` (`ErrorKind.Unavailable`), que a API
  devolve como **HTTP 503 com `Retry-After: 1`** (`application/problem+json`,
  `urn:applada:problem:lock-timeout`) e que incrementa a métrica `matches.lock_timeouts`. O padrão é 5 s
  (`Matches:LockTimeout`, aceito entre 1 ms e 1 min).
- **`CHECK` como defesa em profundidade.** As regras estão no domínio e o banco as repete:
  `capacity` entre 1 e 200, `duration_minutes` entre 15 e 600, `confirmed_count` entre 0 e `capacity`,
  `waitlist_count` entre 0 e 50, `status` e `visibility` em conjuntos fechados, e `ck_registrations_status`.
  Um bug de aplicação que tentasse lotar a partida aborta a transação em vez de gravar estado inválido.
  `UNIQUE(match_id, user_id)` e `UNIQUE(match_id, queue_seq)` em `registrations`, junto com
  `PRIMARY KEY(user_id, key)` em `idempotency_keys`, completam as garantias.
- **Ordem FIFO da fila** vem de `queue_seq`, incrementado a partir de `matches.next_queue_seq` sob o lock;
  não de `now()`, que é o início da transação e poderia inverter a ordem.
- **Concorrência otimista (`version`)** só para edições do organizador (`PATCH`, cancelamento). `xmin` não
  é usado porque inscrições alteram contadores na mesma linha e fariam o organizador receber 412 a cada
  inscrição.
- **Sem retry automático do Npgsql/EF Core.** `EnableRetryOnFailure` não é usado: a estratégia de retry não
  convive com a transação explícita da unidade de trabalho (`MatchesUnitOfWork`). Falha transitória de banco
  chega ao chamador como erro (500 `application/problem+json`, sem detalhe interno).
- **Idempotência HTTP.** `POST /api/v1/matches` exige o cabeçalho `Idempotency-Key` (1 a 100 caracteres ASCII
  imprimíveis; ausente ou fora do formato é 400 `idempotency-key-missing`). A chave é por usuário
  (`PRIMARY KEY(user_id, key)` em `matches.idempotency_keys`) e guarda o hash do pedido, o status e o corpo da
  resposta. Repetição com o mesmo pedido devolve a resposta gravada; mesma chave com pedido diferente é 422
  `idempotency-key-reused`. A **retenção é de 24 h**: chave mais antiga conta como ausente e é apagada, na
  mesma transação, antes de ser gravada de novo. Duas requisições simultâneas com a mesma chave criam uma
  partida só: a perdedora viola a PK (`IdempotencyConflictException`), refaz a leitura e devolve a mesma
  resposta. O serviço `IdempotencyCleanupService` remove as chaves vencidas na subida e depois a cada
  hora, em lotes de mil pelo índice de `created_at`. A limpeza vem habilitada por padrão e pode ser
  desativada com `Matches:IdempotencyCleanupEnabled=false`.
- **Limites de entrada e abuso.** Escritas (`POST /matches`, `PATCH /matches/{id}`, cancelamento, inscrição e
  saída) passam pela política de rate limiting `writes`: janela fixa de 1 min por usuário interno, 30
  requisições por padrão (`Matches:WriteRateLimitPerMinute`), 429 `rate-limited` com `Retry-After`. Corpo de
  `POST /matches` e `PATCH /matches/{id}` limitado a 16 KB (413 `application/problem+json`; o limite vale com
  ou sem `Content-Length`).

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
  quem estoura recebe `LockTimeout` (503 com `Retry-After`). Dimensionado para dezenas de pessoas por partida;
  não há teste de carga que meça o limite.
- O banco é ponto único de falha: sem retry automático e sem réplica definida, banco fora do ar significa
  API sem escrita. Backup, alta disponibilidade e RPO/RTO: a decidir.
- Numa corrida com a mesma `Idempotency-Key`, o segundo INSERT na PK de `idempotency_keys` espera, limitado
  pelo `lock_timeout` (padrão 5 s, `MatchesPersistenceOptions`); se estourar, vira 503 com `Retry-After`, e a
  repetição pelo cliente recebe a resposta gravada.
- O vencimento das chaves é aplicado na leitura. A remoção física por limpeza é assíncrona e pode
  ficar para uma execução posterior se o banco estiver indisponível.
- O rate limiting cobre só as escritas (janela fixa por usuário, em memória do processo: com várias réplicas
  o limite efetivo multiplica). Leituras não têm limite.
- Os `CHECK` duplicam regras do domínio; mudar um limite exige migration e mudança de domínio (ampliar `visibility`, por
  exemplo, pede migration expansiva).
- PostGIS está previsto mas sem uso; o benefício é futuro.

## Referências

- Arquitetura: `docs/architecture/arquitetura-v1.md` (modelo de dados e integridade). O desenho detalhado
  desta fatia vive fora do repositório, de propósito.
- Contrato HTTP: `docs/api/openapi-v1.json`
- Código: `backend/src/Modules/Matches/Applada.Modules.Matches.Infrastructure/MatchesUnitOfWork.cs`,
  `MatchesSession.cs`, `MatchesDbContext.cs`, `Configurations/`;
  `backend/src/Modules/Matches/Applada.Modules.Matches.Api/` (`MatchEndpoints.cs`, `ProblemResults.cs`);
  `backend/src/Applada.Api/Security/RateLimitingSetup.cs`
- Testes (em `backend/tests/Integration/Applada.Api.IntegrationTests/`): `MatchesPersistenceTests.cs`,
  `RegistrationConcurrencyTests.cs`, `MatchesLockTimeoutApiTests.cs`, `MatchesLifecycleTests.cs`,
  `RateLimitingTests.cs`, `MatchesGuardTests.cs`
