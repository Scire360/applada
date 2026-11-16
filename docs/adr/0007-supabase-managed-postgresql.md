# ADR-0007: Supabase como PostgreSQL gerenciado para desenvolvimento

- **Status:** Aceita para desenvolvimento; região do projeto ainda precisa ser confirmada
- **Data:** 15/11/2026

## Contexto

O Applada escolheu PostgreSQL + PostGIS como fonte de verdade e ASP.NET Core como backend.
O repositório ainda não tinha uma API nem migrations. O Supabase foi escolhido para hospedar
um banco de desenvolvimento; a modelagem de domínio continua pendente do workshop dos módulos.

## Decisão

- Usar o Supabase somente como PostgreSQL gerenciado neste estágio. Supabase Auth não faz parte
  desta decisão; a estratégia OIDC permanece em aberto conforme ADR-0004.
- Somente o backend ASP.NET Core acessa o banco. Web e mobile não recebem connection strings,
  `service_role` ou credenciais do PostgreSQL.
- A API recebe `ConnectionStrings:Applada` via User Secrets no desenvolvimento e via secret
  manager no deploy.
- A API expõe probes de liveness e readiness. O probe de readiness executa apenas `SELECT 1`;
  migrations não são executadas automaticamente na inicialização.
- O driver Npgsql é usado pela probe. Cada módulo continuará dono do próprio `DbContext`, schema
  e migrations com EF Core, conforme a arquitetura.
- O projeto Free é apenas para desenvolvimento e testes. Uso em produção depende de decisão
  própria sobre região, custos, backups, restauração, RPO/RTO e requisitos de privacidade.
- A região do projeto destinado à aplicação deve ser confirmada antes de guardar dados reais.
  São Paulo (`sa-east-1`) é a região solicitada; a região observada no projeto anterior era
  Virginia (`us-east-1`).

## Consequências

- A conexão pode ser validada sem criar tabelas nem alterar o schema remoto.
- Não há persistência funcional de partidas até o workshop definir o modelo e as migrations.
- A connection string e qualquer chave permanecem fora do frontend e do controle de versão.
- A localização do projeto deve ser verificada antes de qualquer dado de usuário ser gravado.

## Alternativas consideradas

- PostgreSQL local ou em VPS: compatível com a arquitetura, mas exige operação própria desde o
  início.
- Conectar o frontend diretamente ao Supabase: rejeitada porque contornaria a API .NET e os
  limites de autorização/ownership do Applada.
