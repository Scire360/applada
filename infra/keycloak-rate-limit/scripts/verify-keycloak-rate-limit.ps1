[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [uri]$KeycloakBaseUrl,

    [Parameter(Mandatory)]
    [string]$Realm,

    [string]$ClientId = "applada-web",

    [string]$ExpectedBrowserFlow = "applada-browser-ip-rate-limit",

    [string]$ExpectedFormsFlow
)

$ErrorActionPreference = "Stop"

$ipAddress = $null
$isLoopbackHost = $KeycloakBaseUrl.DnsSafeHost -ieq "localhost"
if ([System.Net.IPAddress]::TryParse($KeycloakBaseUrl.DnsSafeHost, [ref]$ipAddress)) {
    $isLoopbackHost = [System.Net.IPAddress]::IsLoopback($ipAddress)
}

if (-not $KeycloakBaseUrl.IsAbsoluteUri -or
    -not [string]::IsNullOrEmpty($KeycloakBaseUrl.UserInfo) -or
    -not [string]::IsNullOrEmpty($KeycloakBaseUrl.Query) -or
    -not [string]::IsNullOrEmpty($KeycloakBaseUrl.Fragment) -or
    ($KeycloakBaseUrl.Scheme -cne "https" -and
        -not ($KeycloakBaseUrl.Scheme -ceq "http" -and $isLoopbackHost))) {
    throw "Use an HTTPS Keycloak URL. HTTP is allowed only for localhost or a loopback IP during local development."
}

$accessToken = $env:APPLADA_KEYCLOAK_ADMIN_ACCESS_TOKEN
if ([string]::IsNullOrWhiteSpace($accessToken)) {
    throw "Set APPLADA_KEYCLOAK_ADMIN_ACCESS_TOKEN to a short-lived Keycloak admin access token."
}

if ([string]::IsNullOrWhiteSpace($ExpectedFormsFlow)) {
    $ExpectedFormsFlow = "$ExpectedBrowserFlow forms"
}

$baseUrl = $KeycloakBaseUrl.AbsoluteUri.TrimEnd("/")
$headers = @{ Authorization = "Bearer $accessToken" }
$escapedRealm = [uri]::EscapeDataString($Realm)
$realmInfo = Invoke-RestMethod `
    -Method Get `
    -Uri "$baseUrl/admin/realms/$escapedRealm" `
    -Headers $headers

if ($realmInfo.browserFlow -cne $ExpectedBrowserFlow) {
    throw "Realm '$Realm' must use the '$ExpectedBrowserFlow' Browser flow."
}

$flowsResponse = Invoke-RestMethod `
    -Method Get `
    -Uri "$baseUrl/admin/realms/$escapedRealm/authentication/flows" `
    -Headers $headers
$flows = @($flowsResponse)
while ($flows.Count -eq 1 -and $flows[0] -is [System.Array]) {
    $flows = @($flows[0])
}
$browserFlows = @($flows | Where-Object { $_.alias -ceq $ExpectedBrowserFlow })
if ($browserFlows.Count -ne 1 -or [string]::IsNullOrWhiteSpace($browserFlows[0].id)) {
    throw "Expected exactly one top-level Browser flow named '$ExpectedBrowserFlow'."
}

$escapedFlowId = [uri]::EscapeDataString([string]$browserFlows[0].id)
$resolvedBrowserFlow = Invoke-RestMethod `
    -Method Get `
    -Uri "$baseUrl/admin/realms/$escapedRealm/authentication/flows/$escapedFlowId" `
    -Headers $headers
if ($resolvedBrowserFlow.id -cne $browserFlows[0].id -or
    $resolvedBrowserFlow.alias -cne $ExpectedBrowserFlow -or
    -not $resolvedBrowserFlow.topLevel) {
    throw "The resolved Browser flow identity does not match '$ExpectedBrowserFlow'."
}

$escapedFlowAlias = [uri]::EscapeDataString($ExpectedBrowserFlow)
$executionsResponse = Invoke-RestMethod `
    -Method Get `
    -Uri "$baseUrl/admin/realms/$escapedRealm/authentication/flows/$escapedFlowAlias/executions" `
    -Headers $headers
$executions = @($executionsResponse)
while ($executions.Count -eq 1 -and $executions[0] -is [System.Array]) {
    $executions = @($executions[0])
}
$activeFlowAliases = @{}
$formsSubflow = $null
$rateLimitExecutions = @()
$unprotectedPasswordExecutions = @()

foreach ($execution in $executions) {
    $level = [int]$execution.level
    foreach ($childLevel in @($activeFlowAliases.Keys)) {
        if ([int]$childLevel -gt $level) {
            $activeFlowAliases.Remove($childLevel)
        }
    }

    if ($execution.authenticationFlow) {
        if ($level -eq 0 -and $execution.displayName -ceq $ExpectedFormsFlow) {
            $formsSubflow = $execution
        }
        $activeFlowAliases[$level + 1] = [string]$execution.displayName
        continue
    }

    $parentFlowAlias = $activeFlowAliases[$level]
    if ($parentFlowAlias -cne $ExpectedFormsFlow) {
        continue
    }

    if ($execution.providerId -ceq "applada-ip-rate-limit") {
        $rateLimitExecutions += $execution
    }
    elseif ($execution.providerId -ceq "auth-username-password-form" -and
        $execution.requirement -cne "DISABLED") {
        $unprotectedPasswordExecutions += $execution
    }
}

if (-not $formsSubflow -or $formsSubflow.requirement -cnotin @("ALTERNATIVE", "REQUIRED")) {
    throw "The '$ExpectedFormsFlow' Forms subflow must be enabled."
}
if ($rateLimitExecutions.Count -ne 1 -or $rateLimitExecutions[0].requirement -cne "REQUIRED") {
    throw "The rate-limit authenticator must appear exactly once as REQUIRED in '$ExpectedFormsFlow'."
}
if ($unprotectedPasswordExecutions.Count -gt 0) {
    throw "Disable the built-in username/password authenticator in '$ExpectedFormsFlow'."
}

$allClients = @()
$pageSize = 100
for ($first = 0; ; $first += $pageSize) {
    $clientPageResponse = Invoke-RestMethod `
        -Method Get `
        -Uri "$baseUrl/admin/realms/$escapedRealm/clients?first=$first&max=$pageSize" `
        -Headers $headers
    $clientPage = @($clientPageResponse)
    while ($clientPage.Count -eq 1 -and $clientPage[0] -is [System.Array]) {
        $clientPage = @($clientPage[0])
    }

    $allClients += $clientPage
    if ($clientPage.Count -lt $pageSize) {
        break
    }
}
$matchingClients = @($allClients | Where-Object { $_.clientId -ceq $ClientId })

if ($matchingClients.Count -ne 1) {
    throw "Expected exactly one Keycloak client named '$ClientId' in realm '$Realm'."
}

$enabledDirectGrantClients = @($allClients | Where-Object { $_.directAccessGrantsEnabled })
if ($enabledDirectGrantClients.Count -gt 0) {
    $clientNames = @($enabledDirectGrantClients | ForEach-Object { $_.clientId })
    throw "Direct Access Grants must be disabled for every client in realm '$Realm'. Enabled clients: $($clientNames -join ', ')."
}

Write-Output "Keycloak rate-limit flow and Direct Access Grants prerequisites verified for realm '$Realm'."
