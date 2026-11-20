# ADR-0004: OIDC com Keycloak e sessão BFF na web

- **Status:** Aceita
- **Data:** 19/11/2026

## Contexto

O Applada precisa autenticar pessoas na web sem receber nem armazenar suas senhas.
A estratégia OIDC/OAuth2 com PKCE estava definida na arquitetura, mas o provedor
de identidade ainda estava em aberto. O frontend usa Next.js com o padrão BFF; o
backend ainda não possui endpoints de domínio.

## Decisão

- Usar Keycloak como provedor OpenID Connect, com Authorization Code Flow,
  PKCE, `state` e `nonce`.
- Usar Auth.js no servidor Next.js como BFF. Tokens de sessão ficam em cookie
  HttpOnly, Secure em produção e SameSite; tokens do provedor não são expostos à
  camada cliente.
- Exigir `AUTH_SECRET` com pelo menos 32 bytes e `AUTH_URL` HTTPS em produção;
  limitar a sessão local a uma hora de inatividade.
- Ao sair, limpar a sessão local e redirecionar ao endpoint de logout do Keycloak
  server-to-server com `id_token_hint`, sem expor o token ao JavaScript ou
  adicioná-lo à sessão enviada ao frontend. ID e access tokens ficam apenas no
  JWT Auth.js criptografado e protegido pelo cookie HttpOnly. Validar o access
  token e atualizar os papéis publicados no UserInfo do Keycloak em cada
  verificação autenticada revoga cópias do cookie após o fim da sessão SSO e
  reflete mudanças de papel publicadas nesse endpoint, ao custo de uma chamada
  de rede por verificação e de limitar a sessão ao tempo de vida do access token.
- Proteger as rotas do produto no Proxy e validar a sessão também no `AppShell`.
- Usar as telas hospedadas pelo Keycloak para credenciais e autenticação. O
  cadastro usa `prompt=create`; a recuperação de senha começa no login do
  Keycloak, onde a pessoa seleciona a opção de recuperação.
- Manter o cliente OIDC e seu segredo apenas no servidor, configurados por
  variáveis de ambiente/secret manager.

## Consequências

- Cada ambiente precisa de um realm e cliente Keycloak configurados, com
  registro e recuperação habilitados conforme a política do produto.
- O callback local é
  `http://localhost:3000/api/auth/callback/keycloak`; o callback de produção
  deve ser cadastrado explicitamente no cliente.
- O cliente OIDC precisa permitir a post-logout redirect URI HTTPS da aplicação.
  O logout encerra a sessão SSO no Keycloak, mas access tokens JWT já emitidos
  podem continuar válidos em validadores offline até expirarem.
- O backend atual contém apenas probes de saúde. Antes de adicionar endpoints
  com dados de usuário, a API deve validar tokens e aplicar autorização por
  recurso/operação. A sessão web não substitui essa validação.

## Alternativas consideradas

- Supabase Auth: não escolhido; ADR-0007 usa Supabase somente como PostgreSQL.
- Implementar credenciais diretamente no Applada: rejeitado por duplicar
  responsabilidades de identidade e expor senhas à aplicação.
- Keycloak autogerido foi escolhido em vez de um provedor gerenciado para manter
  controle do fluxo OIDC e evitar dependência de um serviço de identidade ainda
  não selecionado.
