# ADR-0003: Mídia em object storage compatível com S3 + CDN

- **Status:** proposto (2026-11-15) — aguarda aprovação do responsável técnico
- **Decisores:** responsável técnico do Applada

## Contexto

O Applada terá fotos e vídeos de usuários. A arquitetura v1 (seção 7) proíbe guardar a biblioteca de
vídeos só no disco da VPS e exige proteção da mídia privada. Provedor, egress e custo ainda estão em
aberto (seção 10).

## Decisão

- Object storage **compatível com S3**, externo à VPS, com versionamento e lifecycle.
- CDN na frente da mídia pública; mídia privada nunca vira pública.
- Upload por **URL assinada curta**; backend valida MIME real (magic bytes), tamanho e duração.
- Em desenvolvimento, storage S3 local no Docker Compose/Aspire.
- O código depende só da API S3, para trocar de provedor sem reescrever.

## Alternativas consideradas

| Abordagem | Trade-off |
|---|---|
| **S3-compatível + CDN (escolhida)** | Portável, escala, desacopla do disco da VPS. Custo recorrente de storage e egress. |
| Disco da VPS | Barato, mas perde dados com a VPS, não escala e não serve vídeo bem. |
| Serviço de vídeo gerenciado | Menos trabalho de transcodificação; maior custo e lock-in. Reavaliar quando houver volume. |

## Consequências

- Positivas: durabilidade, escala, troca de provedor possível.
- Negativas: custo recorrente (exige aprovação, CLAUDE.md seção 11); pipeline de processamento de
  vídeo ainda por desenhar; provedor e orçamento de egress pendentes.
