# Limite de tentativas de login por IP no Keycloak

Este módulo adiciona um autenticador de senha para o Keycloak 26.8.2. O limite é
aplicado no servidor que valida a senha, não no Next.js:

- Até cinco verificações de senha simultâneas por IP e realm.
- Cinco falhas de senha dentro de uma janela móvel de 15 minutos bloqueiam novas
  tentativas de senha desse IP por 15 minutos.
- Tentativas simultâneas já iniciadas podem terminar; a reserva atômica no Redis
  impede iniciar verificações além do limite enquanto elas estão em andamento e
  é renovada até a validação da senha terminar.
- O bloqueio retorna HTTP 429 com `Retry-After`. Falha do Redis ou IP inválido
  retorna HTTP 503; a autenticação falha fechada.
- O Redis recebe apenas chaves HMAC do par realm/IP, nunca o endereço IP em texto.

O código não altera nem instala automaticamente o Keycloak em execução. O provedor
precisa ser implantado e ativado no fluxo Browser de cada realm. Até essa ativação,
o endpoint não está protegido por esta implementação.

## Ambiente local automatizado

Na raiz do repositório:

```powershell
docker compose -f infra\compose\keycloak-rate-limit.yml up -d
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\infra\keycloak\configure-rate-limit.ps1
.\infra\keycloak-rate-limit\scripts\verify-keycloak-rate-limit.ps1 `
  -KeycloakBaseUrl "http://localhost:8080" `
  -Realm "applada"
```

O Compose inicia Keycloak em `http://localhost:8080`, Redis em `127.0.0.1:6380` e
importa o realm descartável `applada`. A chave local fica em
`infra/compose/.env`, que é ignorado pelo Git.

## Requisitos

- Keycloak 26.8.2 e Java 21.
- Maven 3.9 ou superior.
- Redis compartilhado e privado, com autenticação e TLS em produção.
- Proxy reverso confiável em produção, com o IP do cliente corretamente encaminhado.

O Redis é estado de segurança temporário. Use uma instância dedicada ou namespace
isolado, política `noeviction`, autenticação, rede privada e persistência compatível
com o requisito de manter bloqueios após reinício. O limite falha fechado quando o
Redis está indisponível.

## Build e testes

Na raiz do repositório:

```powershell
mvn -f infra\keycloak-rate-limit\pom.xml clean verify
```

Os testes de integração iniciam Redis real com Testcontainers; Docker precisa estar
em execução. Sem Docker, esses testes são ignorados e a verificação não comprova o
comportamento real do Redis.

O JAR sombreado é gerado em:

```text
infra/keycloak-rate-limit/target/applada-keycloak-ip-rate-limit.jar
```

Para desenvolvimento local, inicie o Redis sem persistência e acessível somente
pela máquina local:

```powershell
docker compose -f infra\keycloak-rate-limit\compose.redis.yml up -d
```

Esse serviço não deve ser usado em produção.

## Configuração do servidor

Configure estas variáveis no processo do Keycloak, usando o gerenciador de segredos
do ambiente. O arquivo `.env.example` mostra apenas os nomes; não coloque valores
reais no repositório.

- `APPLADA_AUTH_RATE_LIMIT_REDIS_URL`: URI `redis://` apenas em desenvolvimento
  local; use `rediss://` com credenciais em produção.
- `APPLADA_AUTH_RATE_LIMIT_HMAC_KEY`: chave Base64 que decodifica para pelo menos
  32 bytes. Gere uma chave independente por ambiente e mantenha-a estável enquanto
  houver bloqueios ativos.

Copie o JAR para `<KC_HOME>/providers` e recompile a distribuição otimizada:

```powershell
Copy-Item infra\keycloak-rate-limit\target\applada-keycloak-ip-rate-limit.jar "$env:KC_HOME\providers\"
& "$env:KC_HOME\bin\kc.bat" build
```

No console de administração do Keycloak:

1. Duplique o fluxo Browser do realm; não altere diretamente o fluxo padrão.
2. No subfluxo Forms, substitua `Username Password Form` pelo autenticador
   `Applada Username Password Form with IP Rate Limit`, com requisito `Required`.
3. Configure o fluxo duplicado como Browser flow do realm.
4. Desative `Direct access grants` em todos os clientes do realm. Esse grant não
   passa pelo formulário Browser e, portanto, não é protegido por este autenticador.
5. Publique também o tema Applada atualizado para que os erros de bloqueio e de
   indisponibilidade apareçam em português.

Antes de ativar o fluxo ou promover a configuração do realm, execute a verificação
com um token administrativo de curta duração. Ela falha se o realm não usar o
fluxo Browser protegido ou se Direct Access Grants estiver ativo em qualquer cliente:

```powershell
$env:APPLADA_KEYCLOAK_ADMIN_ACCESS_TOKEN = "<token-admin-de-curta-duracao>"
.\infra\keycloak-rate-limit\scripts\verify-keycloak-rate-limit.ps1 `
  -KeycloakBaseUrl "https://keycloak.example.com" `
  -Realm "applada"
Remove-Item Env:\APPLADA_KEYCLOAK_ADMIN_ACCESS_TOKEN
```

Trate qualquer código de saída diferente de zero como bloqueio de implantação.
O autenticador não intercepta Direct Access Grants; a verificação deve fazer
parte do procedimento de implantação ou liberação do realm. Em produção, use
sempre HTTPS; HTTP é aceito pelo script somente para `localhost` ou IP de loopback
em desenvolvimento local.

O autenticador preserva o suporte a passkeys do formulário padrão. A contagem se
aplica a submissões do formulário de usuário e senha rejeitadas pelo Keycloak,
incluindo usuário inexistente e credenciais inválidas. Falhas de OTP, passkeys e
ações de cadastro ou recuperação não são contadas como falhas de senha.

## IP do cliente e proxy

Sem proxy, Keycloak usa o endereço da conexão. Atrás de proxy, inicialize o
Keycloak com `--proxy-headers=xforwarded` e
`--proxy-trusted-addresses=<IPs-ou-CIDRs-do-proxy>`. O proxy precisa sobrescrever,
não acrescentar, o cabeçalho `X-Forwarded-For` com o endereço do cliente observado
na conexão. Se houver CDN ou outro balanceador antes dele, configure explicitamente
as faixas confiáveis e a resolução do IP real nessa camada.

Restrinja a porta pública do Keycloak para que somente o proxy confiável consiga
alcançá-la. Não aceite cabeçalhos encaminhados diretamente de clientes. Se o
Keycloak não considerar a conexão confiável, ou não receber um IP numérico, o login
falha fechado com HTTP 503.

## Verificação controlada

Valide em realm e conta descartáveis, nunca com credenciais reais:

1. Faça quatro tentativas sequenciais com credenciais inválidas e confirme que o
   formulário continua aceitando novas tentativas.
2. Faça a quinta tentativa inválida e confirme que ela é registrada.
3. Confirme que a próxima submissão de senha do mesmo IP recebe HTTP 429 e
   `Retry-After` de até 900 segundos.
4. Confirme que outro IP ou realm não herda o bloqueio e que, após o TTL, o acesso
   ao formulário é permitido novamente.

Esse teste funcional não é um teste de carga. A implementação não foi ativada no
Keycloak local ou em produção por este build.
