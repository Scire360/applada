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
