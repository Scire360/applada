# ADR-0008: Limite de tentativas de login por IP no Keycloak

- **Status:** Aceita
- **Data:** 19/11/2026

## Contexto

A aplicação web usa OIDC e redireciona a pessoa para as telas hospedadas pelo
Keycloak. A senha não passa pelo Next.js, portanto um limitador no BFF não consegue
contar falhas de autenticação. A proteção padrão do Keycloak é baseada em conta e
não satisfaz o requisito de bloquear o endpoint de senha por IP.

## Decisão

- Aplicar o limite no autenticador de senha do fluxo Browser do Keycloak.
- Permitir cinco falhas de senha por IP e realm em uma janela móvel de 15 minutos;
  após a quinta falha, bloquear novas verificações de senha daquele IP por
  15 minutos.
- Reservar capacidade atomicamente para tentativas simultâneas, sem contar logins
  bem-sucedidos como falhas.
- Guardar contadores e bloqueios em Redis compartilhado, com scripts Lua atômicos,
  TTL e chaves HMAC do realm/IP. Não armazenar o IP em texto no Redis.
- Retornar HTTP 429 com `Retry-After` para IP bloqueado e falhar fechado com HTTP
  503 quando a origem do IP não for confiável ou o Redis estiver indisponível.
- Em produção, aceitar cabeçalhos encaminhados somente de proxies confiáveis e
  restringir a origem de rede do Keycloak a esses proxies.
- Desativar Direct Access Grants no cliente web para impedir um caminho de senha
  que não passe pelo fluxo Browser protegido.

## Consequências

- A extensão é compilada para Keycloak 26.8.2 e requer Java 21.
- Cada realm precisa instalar a extensão, clonar o fluxo Browser, marcar o novo
  autenticador como `Required` e ativar o fluxo; gerar o JAR não altera um servidor.
- O Redis compartilhado passa a ser dependência de disponibilidade do login; a
  indisponibilidade bloqueia autenticações em vez de desativar silenciosamente a
  proteção.
- Redis precisa de rede privada, autenticação, TLS em produção e política
  `noeviction`. Operadores devem preservar chaves com TTL ao reiniciar o serviço.
- Testes de integração dependem de Redis real via Testcontainers.

## Alternativas consideradas

- **Rate limit no Next.js:** rejeitado; não observa falhas de senha validadas pelo
  Keycloak.
- **Proteção nativa do Keycloak por conta:** útil como defesa adicional, mas não
  implementa bloqueio por IP.
- **Contagem simples no proxy:** rejeitada como controle principal porque conta
  logins válidos e não identifica com confiabilidade falhas de credencial.
