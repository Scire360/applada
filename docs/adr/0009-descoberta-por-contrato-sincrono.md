# ADR-0009: Descoberta por contrato síncrono

- **Status:** Aceita
- **Data:** 21/11/2026

## Contexto

O jogador quer encontrar partidas públicas perto de um local. Discovery conhece os locais e suas
coordenadas. Matches conhece os horários, a visibilidade e as inscrições de cada partida.
Cada módulo consulta apenas as próprias tabelas.

## Decisão

Discovery é dono de `discovery.venues`. O PostgreSQL gera a coluna PostGIS
`location geography(Point,4326)` a partir da longitude e da latitude. O índice GiST
`ix_venues_location` atende às consultas por raio. A migration define a coluna e o índice
em SQL, sem dependência de NetTopologySuite.

Na busca, Discovery seleciona até os 200 locais mais próximos dentro do raio. Se houver locais,
passa seus identificadores a `IOpenMatchCatalog`. Matches devolve uma página de partidas públicas
agendadas para o futuro, ordenadas por horário e identificador. Discovery acrescenta o local e a
distância à resposta. Ao criar uma partida com `venueId`, Matches valida o local pelo contrato
`IVenueDirectory` do Discovery.

## Consequências

A busca usa o estado atual das partidas e depende da disponibilidade do catálogo de Matches.
Em regiões com mais de 200 locais no raio, partidas em locais além do 200º ficam fora de todas
as páginas, mesmo quando os locais mais próximos não têm partidas.

## Alternativas consideradas

Manter uma projeção por eventos em `discovery.search_projection`, com publicação de
`VenuePublished`, exigiria consumo idempotente, tratamento de reentregas e recuperação de falhas.
A projeção e o evento ficam adiados até que métricas de latência ou de cobertura justifiquem
esse custo.
