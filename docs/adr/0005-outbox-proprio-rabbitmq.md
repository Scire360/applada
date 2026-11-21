# ADR-0005: Transactional outbox próprio + RabbitMQ

- **Status:** aceito (2026-11-13; revisado em 2026-11-15 com a fatia de Matches completa)
- **Decisores:** responsável técnico do Applada

## Contexto

Módulos se integram por eventos de integração versionados (ADR-0001). O evento precisa nascer na mesma
transação da mudança de estado que o originou; publicá-lo direto no broker depois do commit pode perder o
evento, e publicar antes pode anunciar algo que sofreu rollback. A arquitetura v1 prevê transactional outbox
com RabbitMQ.

A fatia exclui dependências com licença comercial ou fim de manutenção. A biblioteca mais usada para
isso em .NET, MassTransit, tem a v9 comercial e a manutenção oficial da v8 termina após 2026.

## Decisão

**Outbox próprio por schema de módulo, com publicador sobre `RabbitMQ.Client` 7.**

- Tabela `<schema>.outbox_messages` por módulo (`identity`, `matches`), gravada na mesma transação da
  operação. Colunas: `id` (= `eventId`), `event_type`, `schema_version`, `aggregate_id`, `occurred_at`,
  `correlation_id`, `payload` (jsonb), `published_at`, `attempts`, `last_error`, `next_attempt_at`.
- Um `BackgroundService` por módulo no processo da API (`OutboxPublisherService` → `OutboxProcessor<TContext>`).
  Cada lote, numa transação: reserva até 100 mensagens vencidas com `FOR UPDATE SKIP LOCKED` (seguro com
  várias réplicas), publica cada uma com **publisher confirms** no exchange topic durável `applada.events`,
  routing key = `eventType`, mensagens persistentes, e grava o resultado antes do commit.
- **Falha de publicação:** `attempts++`, `last_error` com tipo e mensagem da exceção (nunca o payload) e
  backoff exponencial `min(2^attempts s, 5 min)`.
  - Recusa do broker (nack, `IntegrationEventRejectedException`): só aquela mensagem falha; o lote continua.
  - Qualquer outra falha (conexão, canal, timeout): a mensagem corrente é marcada e **o lote termina**, para
    não manter a transação aberta esperando um broker fora do ar. As demais ficam intactas para o próximo ciclo.
- **Garantia: at-least-once.** Se o commit falhar depois da confirmação do broker, a mensagem é publicada
  de novo. Não há garantia de ordem entre eventos. Consumidores devem deduplicar por `eventId` e tolerar
  reordenação.
- Limpeza: o processador apaga mensagens publicadas há mais de 7 dias na primeira passagem
  e depois, por padrão, no máximo uma vez por hora por processador.
- Envelope: `eventId`, `eventType`, `schemaVersion`, `aggregateId`, `occurredAt`, `correlationId`, `payload`;
  snapshot de contrato por evento v1 nos testes.
- **Eventos publicados hoje** (routing key = `eventType`): `identity.user-registered.v1` e, de Matches,
  `matches.match-created.v1`, `matches.match-cancelled.v1`, `matches.spot-confirmed.v1` e
  `matches.spot-released.v1`. Cada um é gravado no outbox na mesma transação do comando que o originou (criar,
  cancelar, inscrever, sair, promover da espera).
- Métricas do meter `Applada.Outbox`: `outbox.published`, `outbox.pending`, `outbox.publish_failures` e
  `outbox.publish_latency`. `correlationId` vem do trace da requisição e acompanha o evento.

### Desvio deliberado do desenho da fatia: health check do RabbitMQ

O desenho da fatia pede health checks de PostgreSQL e RabbitMQ. O check do RabbitMQ está em `/health/ready`, mas
registrado com `failureStatus: HealthStatus.Degraded`: com o broker fora, `/health/ready` responde **HTTP 200
com status `Degraded`** no corpo, e não 503.

Motivo: o outbox existe justamente para a API continuar aceitando escritas com o broker indisponível. Se a
indisponibilidade do RabbitMQ devolvesse 503, o balanceador tiraria todas as réplicas de rotação ao mesmo
tempo e transformaria uma falha tolerável em indisponibilidade total. A visibilidade que o desenho queria
continua, no corpo da resposta e na métrica `outbox.pending`. Reverter é mudar uma linha em
`backend/src/Applada.Api/Program.cs`. O PostgreSQL continua com status de falha padrão, pois sem ele a API
não atende.

## Alternativas consideradas

| | Abordagem | Trade-off |
|---|---|---|
| **A (escolhida)** | Outbox próprio por schema de módulo + publicador sobre `RabbitMQ.Client` 7 | Controle total, sem risco de licença, sem convenções de framework. Custo: retry e topologia por nossa conta (pouco código, testável com Testcontainers). |
| B | Wolverine (MIT) com outbox EF Core e transporte RabbitMQ | Menos código; porém amarra ao modelo de handlers/codegen do Wolverine, com tabelas próprias e mudanças frequentes entre versões major. Considerado e rejeitado. |
| (descartada) | MassTransit | v9 comercial; manutenção oficial da v8 encerra após 2026. |
| (descartada) | Publicar direto no broker após o commit | perde evento se o processo cair entre commit e publicação |

## Consequências

**Positivas**
- Evento e mudança de estado são atômicos; rollback não deixa evento.
- Sem dependência de licença comercial; poucos componentes para entender e testar.
- Publicador seguro com várias réplicas (`SKIP LOCKED`); API continua escrevendo com o broker fora.

**Negativas e limites conhecidos**
- **Sem consumidores, inbox nem DLQ nesta fatia.** Os eventos só são publicados; DLQ e idempotência de
  consumo entram com o primeiro consumidor real (Notifications). Os projetos de worker em
  `backend/src/Workers` são só pastas vazias. Hoje a publicação pode ocorrer sem fila ligada ao exchange; a
  mensagem é então descartada pelo broker (`mandatory=false`) e fica marcada como publicada.
- **Mensagem envenenada (recusada para sempre) é tentada indefinidamente**, a cada 5 minutos após atingir o
  teto do backoff, sem estado terminal, sem quarentena e sem alerta dedicado além da métrica de falhas.
- Duplicatas são esperadas (at-least-once); a responsabilidade de deduplicar é do consumidor.
- A transação do lote fica aberta durante a publicação; por isso o lote encerra na primeira falha que não
  seja nack.
- A limpeza periódica ainda varre a tabela por `published_at`, que não tem índice. O custo deve ser
  reavaliado conforme o volume de mensagens crescer.
- Sem teste de carga: vazão do publicador e latência sob volume não foram medidas.
- O PostgreSQL é ponto único de falha também para a publicação (o outbox vive nele).
- Retry, reconexão e topologia são código nosso; bugs nesses pontos são nossos para corrigir.

## Referências

- Arquitetura: `docs/architecture/arquitetura-v1.md` (eventos de integração). O desenho detalhado desta
  fatia, incluindo a comparação de bibliotecas, vive fora do repositório, de propósito; a tabela acima a resume.
- Código: `backend/src/BuildingBlocks/Applada.BuildingBlocks.Messaging/` (`OutboxProcessor.cs`,
  `OutboxOptions.cs`, `RabbitMqEventPublisher.cs`, `RabbitMqHealthCheck.cs`),
  `backend/src/BuildingBlocks/Applada.BuildingBlocks.Persistence/Outbox/`, `backend/src/Applada.Api/Program.cs`;
  contratos em `backend/src/Modules/*/Applada.Modules.*.Contracts/`
- Testes: `backend/tests/Integration/Applada.Api.IntegrationTests/OutboxTests.cs`,
  `backend/tests/Contract/Applada.Contracts.Tests/` (formato dos eventos de Identity e Matches)
