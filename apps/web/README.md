# Applada Web

Frontend em Next.js, React e TypeScript estrito. As telas atuais são uma prévia de interface com dados ilustrativos; ainda não há integração com a API.

## Desenvolvimento local

```powershell
cd apps\web
npm install
npm run dev
```

O servidor local fica disponível em `http://localhost:3000`.

## Autenticação web

A web usa Auth.js como BFF com OIDC/PKCE e Keycloak. A senha é digitada somente
na página hospedada pelo Keycloak; a aplicação mantém apenas a sessão protegida
por cookie HttpOnly. As rotas do produto exigem sessão. A tela do Applada
permite entrar e recuperar senha, mas não exibe uma opção de
cadastro. Isso não desativa o autocadastro do Keycloak: se ele estiver habilitado
no realm, a própria tela do provedor ainda poderá oferecê-lo. Para recuperar a
senha, use o link de recuperação na página do Keycloak.

As rotas `/administracao/*` exigem a role de cliente `applada-admin` no cliente
OIDC `applada-web`; contas sem essa role são redirecionadas para `/acesso-negado`.
A API .NET ainda não possui endpoints de domínio: aplique autorização também no
backend quando esses endpoints forem implementados.

1. Copie `.env.example` para `.env.local`.
2. Gere `AUTH_SECRET` com `npx auth secret` e configure-o somente em `.env.local`
   (em produção, use o secret manager).
3. Configure `AUTH_KEYCLOAK_ISSUER` com a URL do issuer do realm e preencha
   `AUTH_KEYCLOAK_ID` e `AUTH_KEYCLOAK_SECRET` do cliente OIDC confidencial.
4. No Keycloak, habilite o fluxo padrão, PKCE S256 e recuperação de senha.
   Habilite o registro no realm somente se o autocadastro for desejado. Cadastre
   `http://localhost:3000/api/auth/callback/keycloak` como redirect URI válido.
   Restrinja as origens e redirects ao domínio da aplicação.
5. Copie `keycloak-theme/applada` para `<KC_HOME>/themes/applada`, selecione
   `applada` como tema de login do realm e defina o idioma padrão como `pt-BR`.
   O tema preserva os templates padrão do Keycloak e aplica a paleta, tipografia
   e composição editorial do Applada. As ações de entrar, cadastrar e recuperar
   senha iniciam o Keycloak em português do Brasil; o seletor de idioma mantém
   inglês como alternativa.
6. Reinicie `npm run dev`. Sem as variáveis necessárias, a tela informa que o
   acesso não está configurado.

`AUTH_TRUST_HOST=true` é adequado para desenvolvimento local. Em produção,
configure-o somente atrás de um proxy confiável, junto do host público correto.
O logout encerra a sessão local; ele não encerra a sessão SSO do Keycloak.

O limite de tentativas de senha por IP precisa ser instalado e ativado no Keycloak.
Consulte [`infra/keycloak-rate-limit/README.md`](../../infra/keycloak-rate-limit/README.md)
para build, Redis, fluxo Browser e configuração de proxy confiável. Sem essa ativação
no realm, o código OIDC da web não limita tentativas de senha.

Este fluxo autentica a aplicação web. A API .NET ainda não possui endpoints de
domínio; qualquer endpoint futuro com dados de usuário precisa validar tokens e
implementar autorização no servidor.

## Verificações

```powershell
npm run lint
npm run typecheck
npm run build
```

## Áreas disponíveis

- `/` — visão geral do grupo
- `/peladas` — agenda e partidas
- `/eventos/novo`, `/peladas/abertas`, `/presenca`, `/presenca/check-in` e `/partidas/ao-vivo` — fluxos de organização das partidas
- `/jogadores` — elenco e presença
- `/jogadores/novo` e `/jogadores/perfil` — convite e perfil
- `/comunidade` — feed do grupo
- `/galeria`, `/aniversarios`, `/notificacoes`, `/ofertas` e `/destaques/votacao` — experiências da comunidade
- `/financeiro` — resumo financeiro demonstrativo
- `/financeiro/lancamentos`, `/financeiro/cobrancas`, `/financeiro/categorias` e rotas relacionadas — gestão financeira demonstrativa
- `/desempenho` e `/relatorios` — estatísticas do grupo
- `/administracao` — acessos, preferências, comunicação, importação e demais ferramentas administrativas
- `/buscar` e `/modulos` — busca e mapa das funcionalidades

O mapa em `/modulos` reúne as 43 vistas identificadas no frontend legado e permite buscar por
nome, caminho ou área do produto. Formulários e listas que precisam de persistência funcionam
apenas como prévia local; não enviam nem armazenam dados. A integração com a API será feita
conforme os contratos publicados em `docs/api`.

## Demonstração da jornada de peladas

Em `/eventos/novo`, crie uma pelada de demonstração com data de hoje ou futura; para hoje, o horário também precisa estar no futuro. Continue pela agenda em `/peladas`.
Em `/peladas/abertas`, `/presenca` e `/presenca/check-in`, é possível simular confirmações e
check-in. A prévia também simula lista de espera e promoção para uma vaga liberada. O estado
existe apenas em memória durante a sessão no navegador: atualizar a página ou abrir outra sessão
reinicia a demonstração. Em `/partidas/ao-vivo`, registre gols e acompanhe o placar; em
`/partidas/ao-vivo`, inicie e encerre a partida depois de pelo menos dois check-ins; o placar
encerrado fica indicado como aguardando conferência. Só então `/destaques/votacao` libera a
escolha de destaque entre as pessoas presentes. Nenhum dado é enviado à API ou gravado no
navegador.
