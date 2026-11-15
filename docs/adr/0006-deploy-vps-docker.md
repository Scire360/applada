# ADR-0006: Deploy em VPS com Docker

- **Status:** proposto (2026-11-15) — aguarda aprovação do responsável técnico
- **Decisores:** responsável técnico do Applada

## Contexto

O lançamento é nacional e a infraestrutura é VPS (arquitetura v1, seção 7). Provedor, regiões,
orçamento, RPO/RTO e política de backup ainda não foram definidos.

## Decisão

- Containers Docker para API, Next.js e workers, **stateless**, atrás de reverse proxy.
- Topologia por maturidade: dev com Compose/Aspire; produção inicial enxuta; depois 2 VPS de
  aplicação com balanceador/edge e failover.
- PostgreSQL em serviço gerenciado ou VPS dedicada, com volume persistente, WAL archival/PITR e backup
  externo criptografado, com teste de restauração. Réplica/failover só após desenho e exercício.
- Mídia em S3 externo (ADR-0003). Segredos em secret manager; `.env` nunca versionado.
- Migrations aplicadas por etapa de deploy, não na subida (`ApplyMigrationsOnStartup=false`).
- OpenTelemetry e CI/CD com rollback.

## Alternativas consideradas

| Abordagem | Trade-off |
|---|---|
| **VPS + Docker (escolhida)** | Custo previsível, simples de operar. HA real exige redundância de banco, proxy e broker. |
| Kubernetes | Escala e automação, mas complexidade e custo operacional altos para a fase atual. |
| PaaS gerenciado | Menos operação, custo maior e menos controle. |

## Consequências

- Positivas: simples, barato, portável.
- Negativas: duas VPS de app não dão alta disponibilidade se banco, proxy ou RabbitMQ forem ponto único
  de falha; operação, patching e backup ficam por nossa conta. Provedor, orçamento e RPO/RTO pendentes;
  custo recorrente e deploy exigem aprovação (CLAUDE.md seção 11).
