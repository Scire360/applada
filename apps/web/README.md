# Applada Web

Frontend em Next.js, React e TypeScript estrito. A agenda demonstrativa usa dados ilustrativos.
`/peladas/conectadas` exercita o módulo Matches com a API e o banco locais.

## Desenvolvimento local

```powershell
cd apps\web
npm install
npm run dev
```

O servidor local fica disponível em `http://localhost:3000`.

## Testar a integração local

1. Na raiz deste checkout, suba o AppHost com Docker aberto seguindo [o README do repositório](../../README.md#executar-o-backend). Confirme que `http://localhost:5080/health/ready` responde `Healthy`.
2. Em outro terminal, habilite o fluxo e abra a web em loopback.

   ```powershell
   cd apps\web
   npm ci
   $env:APPLADA_LOCAL_INTEGRATION = '1'
   npm run dev -- --hostname localhost --port 3000
   ```

   Se a prévia no Brave recarregar repetidamente com o erro `Element type is invalid`
   e o painel indicar Webpack, reinicie o frontend com o comando acima para usar Turbopack.
3. Abra [Partidas conectadas](http://localhost:3000/peladas/conectadas). Entre como `organizador.dev` com a senha temporária `trocar-no-primeiro-login`. O Keycloak pede uma nova senha no primeiro acesso. Para simular a fila, crie uma partida futura com uma vaga e compartilhe seu link em dois perfis ou janelas isoladas, com contas diferentes como `jogador1.dev` e `jogador2.dev`.
4. Confirme a primeira conta, inscreva a segunda na espera, retire a primeira e use **Atualizar partida** na segunda. Os dados vêm do PostgreSQL. O organizador pode editar, cancelar e ver os participantes. A API aplica as restrições de cada papel.

O servidor web usa o client `applada-dev-web` com PKCE e mantém a sessão em memória até o access token expirar ou o servidor reiniciar. O cookie contém um identificador opaco e usa `HttpOnly`, `Secure` e `SameSite=Lax`. O modo exige `npm run dev`, a flag de integração e a origem exata `http://localhost:3000`. Sair encerra a sessão web local. O próximo acesso pede as credenciais novamente no Keycloak.

O contrato de Matches oferece partidas que o usuário organiza ou das quais participa. O fluxo usa links para abrir outras partidas por ID. Check-in, placar e votação seguem na demonstração.

Em **Meu perfil**, o jogador salva seu próprio nome de exibição. O nome aparece na lista de participantes.
A lista continua visível ao organizador e às inscrições ativas. Quem ainda não definiu um nome aparece
pelo identificador.

## Perfil pelo avatar

O avatar do cabeçalho abre **Sua conta**. Em **Perfil**, edite o nome e, depois de escolher um arquivo,
revise a miniatura antes de salvar. O arquivo escolhido deve ter até 1 MB (1.000.000 bytes) e ser JPG,
PNG ou WebP. A aplicação recorta uma miniatura quadrada de 192 × 192. No modo de prévia, o nome e a
foto ficam no navegador. Na conta conectada, o nome fica no PostgreSQL e a miniatura fica no Storage.

Para habilitar a foto da conta conectada, crie no projeto Supabase do Applada um bucket privado
`applada-avatars`, limitado a 1 MB e ao MIME `image/png`. Mantenha a leitura e a escrita diretas do
bucket fechadas para os papéis `anon` e `authenticated`. A aplicação envia e serve a miniatura pelo
BFF, que resolve a identidade no backend e só acessa o objeto da própria conta.

Configure `APPLADA_SUPABASE_URL` e `APPLADA_SUPABASE_STORAGE_KEY` no processo do Next.js ou em
`.env.local` ignorado pelo Git. A chave é secreta e exclusiva do servidor. A capacidade assinada
de upload também fica no servidor. Reinicie o Next.js depois de configurar o ambiente.

## E2E de partida com uma vaga

Com a API, o Keycloak e a web local ativos, conforme os passos acima, instale o Google Chrome e prepare
as contas de teste `jogador1.dev`, `jogador2.dev` e `jogador3.dev` no Keycloak. Em um realm recém-importado,
conclua o primeiro login de cada conta e defina a mesma senha de teste nas três. O teste usa essa senha
apenas no processo local e espera que as contas já tenham concluído a troca obrigatória.

Em outro terminal PowerShell 7.2 ou superior, a partir de `apps/web`:

```powershell
npm ci
$env:APPLADA_E2E_TEST_PASSWORD = Read-Host 'Senha local das três contas de teste' -MaskInput
try {
  npm run test:e2e
} finally {
  Remove-Item Env:APPLADA_E2E_TEST_PASSWORD
}
```

Com um bucket de teste configurado e a conta `jogador2.dev` sem foto, defina
`APPLADA_E2E_AVATAR_STORAGE=1` para o E2E verificar o limite de tamanho, salvar o avatar, conferi-lo
em outra sessão e removê-lo ao final.

O Playwright abre três sessões independentes no Chrome. O segundo jogador salva um nome de exibição
e o teste confirma que o nome persiste após recarregar a página. O organizador cria uma partida com uma vaga.
O teste verifica a confirmação do primeiro jogador, o nome do segundo na lista de espera e sua promoção
após a saída do primeiro. A conta que saiu perde a lista na interface e recebe
`403 participants-forbidden` pela API do frontend.

Ao final, o teste cancela sua partida e restaura o nome anterior do segundo jogador, se havia um.
Se o perfil estava vazio, o nome sintético fica na conta de teste. O registro da partida cancelada fica no
banco local.

## Verificações

Com o app rodando em `http://localhost:3000` e o Chrome instalado, execute
`npm run test:layout` para verificar as páginas estáticas e as rotas do
catálogo em tamanhos de tela entre 320 px e 4K. O teste usa a prévia sem login.

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
nome, caminho ou área do produto. Nas telas demonstrativas, formulários e listas simulam a interação em memória no navegador.

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
