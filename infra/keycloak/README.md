# Keycloak de desenvolvimento

> **Somente desenvolvimento local.** Este diretório é montado (somente leitura) no container
> Keycloak do AppHost Aspire (`backend/src/Applada.AppHost`). Nada aqui vale para ambientes
> compartilhados, homologação ou produção, e nenhum segredo real pode ser colocado aqui.

## O que tem aqui

`applada-dev-realm.json` é importado na subida (`start-dev --import-realm`). O Keycloak exige que o
arquivo de import se chame `<realm>-realm.json`, então o AppHost monta este arquivo (somente leitura)
como `/opt/keycloak/data/import/applada-realm.json`. O container não tem volume: a cada subida do
AppHost o realm volta ao estado deste arquivo (e a troca de senha dos usuários de teste é refeita).

O Keycloak recusa campos desconhecidos no JSON do realm; por isso o aviso `_comentario` ("somente
dev") fica em `attributes` do realm e do client, não na raiz.

- Realm `applada`; issuer `http://localhost:8080/realms/applada`.
- Client público `applada-dev-scalar` (Scalar da API local): só Authorization Code com PKCE `S256`;
  implicit, direct access grants (senha), service account e device flow desligados. Redirect e
  web origin restritos a `http://localhost:5080` (`/scalar` e `/scalar/*`).
- Mapper de audience: o access token sai com `applada-api` em `aud`, que é o
  `Authentication:Audience` da API. `sub` vem do mapper `oidc-sub-mapper`; `iss` é o issuer do realm.
- Usuários de teste `organizador.dev`, `jogador1.dev`, `jogador2.dev` e `jogador3.dev`, com a senha
  temporária pública `trocar-no-primeiro-login`: o Keycloak exige a troca no primeiro login. No
  import, `temporary: true` sozinho não força a troca; quem força é `requiredActions: ["UPDATE_PASSWORD"]`.
  E-mails no domínio reservado `.invalid`. Os usuários são **somente de desenvolvimento**: o sufixo `.dev`
no nome e o domínio `.invalid` no e-mail existem para que nenhum deles seja confundido com conta real nem
receba e-mail; não os recrie fora do ambiente local.
- Rede: as portas publicadas (HTTP 8080 e management 9000) escutam apenas em loopback (`127.0.0.1` e
  `::1`); outra máquina da rede não alcança o Keycloak local.

## Senha do admin do Keycloak

Vem do parâmetro secreto `keycloak-admin-password` do AppHost (usuário `admin`), nunca de arquivo
versionado. Defina uma vez nos user-secrets do AppHost:

```powershell
dotnet user-secrets set "Parameters:keycloak-admin-password" "<senha-local>" --project backend/src/Applada.AppHost
```

Sem ela, o dashboard do Aspire pede o valor ao subir. Console de administração:
`http://localhost:8080/admin/` (realm `master`).

## Uso

```powershell
dotnet run --project backend/src/Applada.AppHost
```

Depois abra `http://localhost:5080/scalar/v1`, autentique com OAuth2 (Authorization Code + PKCE) usando
um dos usuários de teste e chame os endpoints de `/api/v1`.
