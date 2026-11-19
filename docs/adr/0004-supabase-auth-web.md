# ADR-0004: Supabase Auth para autenticação web

- Status: Aceito para web; autenticação mobile permanece pendente
- Data: 19/11/2026

## Contexto

O frontend Next.js precisa autenticar membros sem expor tokens ao JavaScript do navegador. O
provedor deve funcionar com o projeto Supabase já usado para PostgreSQL, sem contornar a API
.NET para acesso a dados de domínio.

## Decisão

- Usar Supabase Auth para login web inicial com e-mail e senha.
- O Next.js atua como BFF: operações de autenticação ocorrem no servidor, usando `@supabase/ssr`.
- A sessão fica em cookie `HttpOnly`, `SameSite=Lax`, `Path=/` e `Secure` em produção.
- O proxy do Next verifica a sessão com `auth.getUser()` antes de permitir rotas do aplicativo.
- Somente `SUPABASE_URL` e a chave pública `SUPABASE_ANON_KEY` são usadas; a chave `service_role`
  nunca é usada no frontend nem neste fluxo.
- Não haverá cadastro público na primeira etapa. Usuários serão provisionados pelo fluxo
  administrativo do Supabase.
- O app não implementa recuperação de senha nesta etapa.
- Autenticação mobile, OIDC/PKCE para provedores externos e federação permanecem decisões futuras.

## Alternativas consideradas

- Keycloak autogerenciado: oferece controle e portabilidade, mas exige operação e manutenção do
  provedor de identidade.
- Outro OIDC gerenciado: mantém flexibilidade, mas adiciona um provedor separado ao estágio inicial.
- Supabase Auth direto do browser: rejeitado para este fluxo web porque exporia tokens ao runtime
  do cliente e diverge do padrão BFF definido para o projeto.

## Segurança e operação

- Respostas de falha de login são genéricas para não confirmar a existência de uma conta.
- As rotas do app falham fechadas quando a configuração está ausente ou não é possível validar a
  sessão.
- Antes de produção, configurar e testar rate limits e CAPTCHA no projeto Supabase, além de alertas
  para abuso. Esta implementação não possui ainda um limitador distribuído próprio.
- URLs e chaves são configuração de ambiente, nunca versionadas. A chave `service_role` é proibida
  neste fluxo.

## Consequências

- O web depende do projeto Supabase para autenticação e disponibilidade de sessão.
- A configuração inicial exige URL e chave pública do projeto no ambiente do servidor Next.js.
- A API ASP.NET Core e os contratos de domínio ainda precisam definir autorização por recurso; estar
  autenticado no web não concede autorização por si só.
- A configuração de autenticação mobile e a estratégia de migração do provedor devem ser decididas
  antes do lançamento mobile.
