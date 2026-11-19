# Applada Web

Frontend em Next.js, React e TypeScript estrito. As telas atuais são uma prévia de interface com dados ilustrativos; ainda não há integração com a API.

## Desenvolvimento local

```powershell
cd apps\web
npm install
npm run dev
```

O servidor local fica disponível em `http://localhost:3000`.

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

## Autenticação web

O acesso inicial usa Supabase Auth com e-mail e senha por uma integração server-side do Next.js.
O navegador recebe somente um cookie de sessão `HttpOnly`; tokens não são armazenados em
`localStorage` ou `sessionStorage`. As rotas do aplicativo falham fechadas e redirecionam para
`/login` quando a sessão não pode ser verificada.

Copie `.env.example` para `.env.local` e configure `SUPABASE_URL` e `SUPABASE_ANON_KEY` com os
valores do projeto Supabase. Essas variáveis são lidas somente pelo servidor Next.js. Não use
uma chave `service_role` e nunca versione `.env.local`.

Ative o provedor de e-mail/senha e crie os usuários pelo fluxo administrativo do Supabase. O
cadastro público e a recuperação de senha ainda não estão implementados. Antes de produção,
configure e valide os limites de autenticação e CAPTCHA disponíveis no projeto Supabase;
esta etapa ainda não adiciona um limitador distribuído próprio.

O runtime do Next.js 16 exige Node.js 20.9 ou superior.

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
