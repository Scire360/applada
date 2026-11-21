# Operação da descoberta de peladas

## Preparar o banco de produção

A migration `AddVenues` executa `CREATE EXTENSION IF NOT EXISTS postgis`. Antes de aplicá-la,
use um papel administrativo para criar a extensão no banco de destino. Assim, o papel que aplica
as migrations pode manter seus privilégios habituais.

As migrations de Matches criam `ix_matches_venue_upcoming` e `ix_matches_group_upcoming`
sem `CONCURRENTLY`. Enquanto cada índice é construído, o PostgreSQL bloqueia escritas em
`matches.matches`. Em produção com volume, aplique as migrations em uma janela de manutenção
ou gere e revise um script com `CREATE INDEX CONCURRENTLY` para cada índice. Nesse script,
preserve as colunas e os predicados das migrations, execute cada criação fora de um bloco de
transação e confirme que os índices estão válidos antes de registrar as migrations como aplicadas.

## Configurar o access log do proxy

No trecho do BFF até a API, o pedido para `/api/v1/discover/matches` leva `lat` e `lon`
já arredondados a duas casas na query. Configure o reverse proxy desse trecho para omitir
a query inteira do access log dessa rota, inclusive em respostas de erro. O método, o caminho
sem parâmetros, o status HTTP e a duração permitem acompanhar a rota sem guardar a localização.
