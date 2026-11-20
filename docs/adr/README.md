# Architecture Decision Records

Cada decisão arquitetural relevante é registrada como `NNNN-titulo-curto.md`
(contexto, decisão, alternativas consideradas, consequências e status).

ADRs planejados (seção 11 da Especificação de Arquitetura v1.0):

| ADR | Tema | Status |
|---|---|---|
| 0001 | [Monólito modular orientado a eventos](0001-monolito-modular.md) | aceito |
| 0002 | [PostgreSQL + PostGIS como fonte de verdade](0002-postgresql-postgis.md) | aceito |
| 0003 | [Mídia em object storage compatível com S3 + CDN](0003-midia-s3-cdn.md) | proposto |
| 0004 | [Autenticação OIDC/OAuth2 com PKCE](0004-oidc-pkce.md) | aceito para web; mobile e política de produção pendentes |
| 0005 | [Transactional outbox próprio + RabbitMQ](0005-outbox-proprio-rabbitmq.md) | aceito |
| 0006 | [Deploy em VPS com Docker](0006-deploy-vps-docker.md) | proposto |
| 0007 | [Identidade interna desacoplada do provedor + Keycloak local para a API](0007-identidade-interna-e-keycloak-dev.md) | aceito |
| 0008 | [Limite de tentativas de login por IP no Keycloak](0008-limite-de-login-por-ip-no-keycloak.md) | aceito |
