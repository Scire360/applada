# Architecture Decision Records

Cada decisão arquitetural relevante é registrada como `NNNN-titulo-curto.md`
(contexto, decisão, alternativas consideradas, consequências e status).

ADRs planejados (seção 11 da Especificação de Arquitetura v1.0):

| ADR | Tema | Status |
|---|---|---|
| 0001 | [Monólito modular orientado a eventos](0001-monolito-modular.md) | aceito |
| 0002 | [PostgreSQL + PostGIS como fonte de verdade](0002-postgresql-postgis.md) | aceito |
| 0003 | Mídia em object storage compatível com S3 + CDN | pendente |
| 0004 | Autenticação OIDC/OAuth2 com PKCE | pendente |
| 0005 | [Transactional outbox próprio + RabbitMQ](0005-outbox-proprio-rabbitmq.md) | aceito |
| 0006 | Deploy em VPS com Docker | pendente |
| 0007 | [Identidade interna desacoplada do provedor + Keycloak só em dev](0007-identidade-interna-e-keycloak-dev.md) | aceito |
