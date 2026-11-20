# ADR-0004: Autenticação OIDC/OAuth2 com PKCE

- **Status:** aceito para web; mobile e política operacional de produção pendentes (2026-11-19)
- **Decisores:** responsável técnico do Applada

## Contexto

A API valida tokens OIDC e mantém a identidade interna desacoplada do provedor
(ADR-0007). A aplicação web precisa autenticar sem receber senhas e sem expor
tokens ao navegador. A implementação web usa Keycloak e Auth.js; as políticas
operacionais do realm de produção e a implementação mobile ainda precisam ser
definidas antes do respectivo lançamento.

## Decisão

- Usar OIDC/OAuth2 Authorization Code com PKCE; não usar fluxo implícito nem
  tratar senhas no Applada.
- **Web:** usar Keycloak como provedor OIDC atual e Auth.js no servidor Next.js
  como BFF. O navegador recebe somente cookie de sessão `HttpOnly`,
  `Secure` em produção e `SameSite`; tokens não são expostos à camada cliente
  nem armazenados em `localStorage`/`sessionStorage`.
- O BFF de domínio valida a sessão Auth.js no servidor e lê o access token
  exclusivamente do JWT criptografado da sessão para encaminhar operações
  allowlisted à API. Não incluir access/ID tokens no callback `session` nem em
  respostas ao navegador. A API continua validando o bearer token e a
  autorização por recurso independentemente.
- Manter `AUTH_SECRET` forte e exclusivo por ambiente; em produção exigir HTTPS
  e configurar somente a origem pública confiável.
- A sessão web dura no máximo uma hora. Nas verificações autenticadas, validar o
  access token e atualizar os papéis pelo UserInfo do Keycloak. Ao sair, encerrar
  a sessão local e a sessão SSO com `id_token_hint`. Tokens JWT já emitidos
  ainda podem ser aceitos por validadores offline até expirarem.
- Proteger rotas administrativas com a role de cliente `applada-admin` e validar
  também a sessão no servidor. MFA é obrigatório para administradores e deve ser
  aplicado na configuração de cada realm antes de produção.
- **API:** validar independentemente JWT Bearer (issuer, audience e assinatura)
  e autorizar cada operação no servidor. A sessão web não autoriza chamadas à
  API; o vínculo com o usuário interno segue o ADR-0007. O client OIDC da web
  deve incluir `applada-api` na audience do access token (`oidc-audience-mapper`).
- **Mobile:** quando implementado, usar Authorization Code com PKCE e guardar
  tokens exclusivamente em `expo-secure-store`, nunca em `AsyncStorage`.
- Configurar tokens de curta duração e rotação/revogação de refresh tokens nos
  ambientes aplicáveis. A política definitiva de produção depende de validação
  operacional e de segurança.

## Alternativas consideradas

| Abordagem | Trade-off |
|---|---|
| **OIDC + PKCE com provedor consolidado** | Padrão, sem guardar senhas; exige operar ou contratar provedor. |
| Auth próprio (senha no Applada) | Controle total, mas risco alto de segurança e LGPD. Descartado. |
| Token em storage do navegador | Simples, mas exposto a XSS. Descartado. |

## Consequências

- A implementação web depende de um realm Keycloak por ambiente e de configuração
  segura de callbacks, logout, roles, MFA e proxy confiável.
- O UserInfo em cada verificação autenticada acrescenta uma chamada ao Keycloak;
  sessões também deixam de funcionar quando o access token expira.
- A leitura server-side do JWT usa o helper `getToken` do Auth.js. Como essa API
  é version-sensitive, upgrades do Auth.js devem executar os testes do BFF e
  confirmar que os tokens continuam restritos ao servidor.
- O limite de tentativas de senha por IP no fluxo Browser é definido no ADR-0008.
- A integração de Identity exige a revisão de segurança aprofundada antes de
  release, conforme `CLAUDE.md` seção 7.
