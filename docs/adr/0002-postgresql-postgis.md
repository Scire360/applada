# ADR-0002: PostgreSQL + PostGIS como fonte de verdade

- Status: Aceito
- Data: 19/11/2026

## Contexto

O dominio exige integridade transacional, concorrencia, indices geograficos e consistencia definitiva.

## Decisao

Usar PostgreSQL com PostGIS como fonte de verdade. Redis sera usado apenas para cache ou aceleracao, nunca para decidir saldo, vaga ou estado definitivo.

Operacoes criticas usarao transacoes, locks ou atualizacoes atomicas conforme o nivel de concorrencia.

## Alternativas consideradas

- Redis como estado principal: contradiz a necessidade de durabilidade e integridade.
- Banco NoSQL: adiciona complexidade para relacoes e regras transacionais.

## Consequencias

- O schema e as migrations permanecem versionados por modulo.
- Geolocalizacao utilizara tipos geograficos PostGIS.
- Escritas sensiveis exigirao testes reais contra PostgreSQL.
