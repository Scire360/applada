// Ambiente local de desenvolvimento (Aspire): PostgreSQL/PostGIS, RabbitMQ, Keycloak e a API.
// Só dev: nada daqui vai para produção. Imagens com tag fixa.

var builder = DistributedApplication.CreateBuilder(args);

// Origem fixa da API: o realm de dev só aceita redirect do Scalar nesta origem (infra/keycloak).
const int ApiHttpPort = 5080;

// Porta fixa do Keycloak: o issuer dos tokens é http://localhost:8080/realms/applada.
const int KeycloakHttpPort = 8080;

var postgres = builder.AddPostgres("postgres")
    .WithImage("postgis/postgis")
    .WithImageTag("17-3.5")
    .WithDataVolume();
var applada = postgres.AddDatabase("applada");

var rabbitmq = builder.AddRabbitMQ("rabbitmq")
    .WithManagementPlugin()
    .WithImageTag("4.4.0-management");

// Aspire.Hosting.Keycloak só existe em preview (spec §16): container genérico da imagem oficial.
// Senha do admin: user-secrets do AppHost (Parameters:keycloak-admin-password); nunca em arquivo versionado.
var keycloakAdminPassword = builder.AddParameter("keycloak-admin-password", secret: true);
var keycloak = builder.AddContainer("keycloak", "quay.io/keycloak/keycloak", "26.8.2")
    .WithArgs("start-dev", "--import-realm")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
    .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", keycloakAdminPassword)
    .WithEnvironment("KC_HEALTH_ENABLED", "true")
    // O import exige o nome "<realm>-realm.json": o arquivo versionado é montado com esse nome.
    .WithBindMount("../../../infra/keycloak/applada-dev-realm.json", "/opt/keycloak/data/import/applada-realm.json", isReadOnly: true)
    .WithBindMount("../../../infra/keycloak/themes/applada", "/opt/keycloak/themes/applada", isReadOnly: true)
    .WithHttpEndpoint(port: KeycloakHttpPort, targetPort: 8080, name: "http")
    .WithHttpEndpoint(targetPort: 9000, name: "management")
    .WithHttpHealthCheck("/health/ready", endpointName: "management");

builder.AddProject<Projects.Applada_Api>("api", launchProfileName: null)
    .WithHttpEndpoint(port: ApiHttpPort, name: "http")
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development")
    .WithReference(applada)
    .WithReference(rabbitmq)
    .WithEnvironment("Authentication__Authority", ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/applada"))
    .WithEnvironment("Authentication__Audience", "applada-api")
    .WithEnvironment("Database__ApplyMigrationsOnStartup", "true")
    .WithHttpHealthCheck("/health/ready")
    .WaitFor(applada)
    .WaitFor(rabbitmq)
    .WaitFor(keycloak);

await builder.Build().RunAsync();
