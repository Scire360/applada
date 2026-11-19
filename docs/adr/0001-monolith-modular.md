# ADR-0001: Monolito modular orientado a eventos

- Status: Aceito
- Data: 19/11/2026

## Contexto

O Applada precisa entregar uma plataforma integrada sem antecipar a separacao operacional de microservicos.

## Decisao

Adotar um monolito modular implantavel como uma unidade, dividido por modulos de negocio. Cada modulo sera dono de suas entidades, schema, DbContext e migrations. Modulos nao acessarao diretamente tabelas ou repositorios uns dos outros.

A API sera composition root. Integracoes entre modulos usarao contratos publicados ou eventos de integracao versionados com outbox transacional.

## Alternativas consideradas

- Microservicos desde o inicio: maior custo operacional e risco prematuro.
- Monolito em camadas por tecnologia: dificulta ownership e evolucao por dominio.

## Consequencias

- A arquitetura permite extracao incremental quando metricas justificarem.
- Testes verificarao dependencias permitidas e proibidas.
- O escopo inicial reduz latencia operacional e complexidade de entrega.
