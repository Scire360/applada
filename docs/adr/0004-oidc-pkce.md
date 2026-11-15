# ADR-0004: Autenticação OIDC/OAuth2 com PKCE

- **Status:** proposto (2026-11-15) — aguarda aprovação do responsável técnico
- **Decisores:** responsável técnico do Applada

## Contexto

A API valida JWT de um provedor OIDC; a identidade interna é desacoplada do provedor (ADR-0007) e o
Keycloak é usado só em desenvolvimento. Falta fixar o protocolo para clientes e o armazenamento de
tokens. A escolha do provedor de produção segue em aberto.

## Decisão

- **OIDC/OAuth2 Authorization Code com PKCE** para web e mobile; nada de fluxo implícito nem de senha
  tratada pelo Applada.
- Access token curto; refresh token rotativo e revogável.
- **Mobile:** tokens no `expo-secure-store`, nunca em `AsyncStorage`.
- **Web:** padrão BFF; o servidor Next.js guarda os tokens e o navegador recebe só cookie
  `HttpOnly`/`Secure`/`SameSite`, com proteção CSRF. Nada em `localStorage`/`sessionStorage`.
- MFA obrigatório para administradores.
- A API só valida o JWT (issuer, audience, assinatura); o vínculo com o usuário interno segue o ADR-0007.

## Alternativas consideradas

| Abordagem | Trade-off |
|---|---|
| **OIDC + PKCE com provedor consolidado (escolhida)** | Padrão, seguro, sem guardar senha. Dependência de um provedor. |
| Auth próprio (senha no Applada) | Controle total, mas risco alto de segurança e LGPD. Descartado. |
| Token em storage do navegador | Simples, mas exposto a XSS. Descartado. |

## Consequências

- Positivas: segurança padrão de mercado, troca de provedor sem mexer no domínio.
- Negativas: BFF adiciona uma peça na web; provedor de produção ainda a decidir (ADR futuro);
  Identity exige varredura `claude-security` a cada mudança (CLAUDE.md seção 7).
