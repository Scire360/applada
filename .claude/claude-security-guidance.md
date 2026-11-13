# Security guidance — Applada

Plataforma de futebol amador e rede social. Backend .NET 10 / EF Core / PostgreSQL+PostGIS,
web Next.js, mobile Expo. Dados de menores podem existir. Sujeito à LGPD.

## Autorização (maior risco)
- Todo endpoint em `backend/src/**/Api` deve verificar se o usuário pode agir **sobre aquele
  recurso** (dono, admin do grupo, participante da partida), não só se está autenticado.
  Buscar recurso por ID sem filtrar pelo ator é IDOR/BOLA.
- `[AllowAnonymous]` só em rotas públicas documentadas (login, descoberta pública).
- Ações administrativas e de moderação (módulo Trust) exigem papel explícito e geram
  registro em `trust.audit_log`.
- Bloqueios entre usuários e visibilidade (público/grupo/convite) devem ser aplicados em
  toda leitura de feed, perfil, posts, comentários e descoberta.

## Dados e concorrência
- Alocação de vaga, lista de espera, saldos e resultados: nunca "lê e depois grava" sem
  lock (`FOR UPDATE`), update condicional ou constraint. Redis não decide estado definitivo.
- SQL só parametrizado. `FromSqlRaw`/`ExecuteSqlRaw` com interpolação ou concatenação é falha.
- Um módulo não pode consultar tabelas/schema de outro módulo.

## Dados pessoais
- Não registrar em log: e-mail, telefone, CPF, coordenadas exatas, tokens, cookies,
  cabeçalho `Authorization`, payload de webhook de pagamento.
- Localização de usuários exposta apenas de forma aproximada; coordenadas exatas só para
  locais (venues) públicos.
- Respostas de erro não expõem stack trace, SQL ou nomes internos.

## Mídia
- Upload via URL assinada de curta duração; validar MIME por magic bytes, tamanho, duração
  e dono na conclusão. Nunca confiar em extensão ou `Content-Type` do cliente.
- Bucket privado nunca público; mídia privada só por URL assinada.
- FFmpeg/processamento roda isolado, com limites de CPU/memória e sem argumentos vindos do
  usuário sem validação (injeção de comando).
- Busca de URL externa (prévia de link, avatar por URL) exige proteção contra SSRF.

- Remover metadados EXIF (especialmente GPS) de toda imagem enviada antes de publicar.
- Conteúdo de usuário (posts, comentários, nomes) sanitizado na renderização; CSP na web.

## Privacidade e abuso
- DTOs de entrada explícitos: nunca aceitar `OwnerId`, `Role`, `Status` ou `UserId` do cliente
  quando o servidor deve definir (mass assignment / overposting).
- Descoberta por distância: arredondar distâncias e coordenadas para impedir trilateração
  da posição de um usuário.
- Convites, cadastro e recuperação de senha não revelam se e-mail/telefone existe
  (enumeração de usuários); respostas e tempos uniformes.
- Push notifications sem PII ou conteúdo privado no corpo.

## Pagamentos
- Nunca armazenar dados de cartão. Webhook do PSP: verificar assinatura, valor, moeda,
  recebedor e deduplicar por ID de evento; rejeitar timestamp fora da janela (replay).
  Retorno do navegador não confirma pagamento.

## Autenticação
- OIDC/OAuth2 com PKCE. Access token curto, refresh rotativo e revogável. MFA obrigatório
  para administradores e moderadores.
- Web: padrão BFF — tokens só no servidor Next.js; navegador recebe cookie `HttpOnly` +
  `Secure` + `SameSite` com proteção CSRF; nunca token em `localStorage`/`sessionStorage`. Mobile: `expo-secure-store`, nunca `AsyncStorage`.
- Comparação de tokens/assinaturas em tempo constante (`CryptographicOperations.FixedTimeEquals`).
- Rate limit em login, recuperação de senha, comentários, convites e uploads.

## Infra e configuração
- Sem segredos em código, `appsettings*.json` versionado, Dockerfile ou workflow de CI.
- Não desabilitar validação TLS (`TrustServerCertificate=true`, `SslMode=Disable`,
  callbacks que aceitam qualquer certificado) fora de ambiente local explícito.
- CORS restrito a origens conhecidas; nada de `AllowAnyOrigin` com credenciais.
- Workflows do GitHub Actions: permissões mínimas, sem `pull_request_target` com checkout
  do código do PR, actions fixadas por SHA.
