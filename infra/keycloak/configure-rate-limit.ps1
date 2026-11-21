param(
    [string]$KeycloakBaseUrl = "http://localhost:8080",
    [string]$Realm = "applada"
)

$ErrorActionPreference = "Stop"

$token = Invoke-RestMethod `
    -Method Post `
    -Uri ($KeycloakBaseUrl.TrimEnd("/") + "/realms/master/protocol/openid-connect/token") `
    -ContentType "application/x-www-form-urlencoded" `
    -Body "grant_type=password&client_id=admin-cli&username=admin&password=admin"

$headers = @{ Authorization = "Bearer $($token.access_token)" }
$base = $KeycloakBaseUrl.TrimEnd("/") + "/admin/realms/" + [uri]::EscapeDataString($Realm)
$customAlias = "applada-browser-ip-rate-limit"

$flows = Invoke-RestMethod -Method Get -Uri ($base + "/authentication/flows") -Headers $headers
if (@($flows | Where-Object { $_.alias -ceq $customAlias }).Count -eq 0) {
    Invoke-RestMethod `
        -Method Post `
        -Uri ($base + "/authentication/flows/browser/copy") `
        -Headers $headers `
        -ContentType "application/json" `
        -Body (@{ newName = $customAlias } | ConvertTo-Json)
}

$executions = Invoke-RestMethod `
    -Method Get `
    -Uri ($base + "/authentication/flows/" + $customAlias + "/executions") `
    -Headers $headers

foreach ($execution in @($executions)) {
    if ($execution.providerId -ceq "auth-username-password-form" -and $execution.level -eq 1) {
        $execution.requirement = "DISABLED"
        Invoke-RestMethod `
            -Method Put `
            -Uri ($base + "/authentication/flows/" + $customAlias + "/executions") `
            -Headers $headers `
            -ContentType "application/json" `
            -Body ($execution | ConvertTo-Json -Depth 20)
    }
}

$executions = Invoke-RestMethod `
    -Method Get `
    -Uri ($base + "/authentication/flows/" + $customAlias + "/executions") `
    -Headers $headers
$customExecutions = @($executions | Where-Object { $_.providerId -ceq "applada-ip-rate-limit" })

if ($customExecutions.Count -eq 0) {
    $formsAlias = [uri]::EscapeDataString($customAlias + " forms")
    Invoke-RestMethod `
        -Method Post `
        -Uri ($base + "/authentication/flows/" + $formsAlias + "/executions/execution") `
        -Headers $headers `
        -ContentType "application/json" `
        -Body (@{ provider = "applada-ip-rate-limit" } | ConvertTo-Json)

    $executions = Invoke-RestMethod `
        -Method Get `
        -Uri ($base + "/authentication/flows/" + $customAlias + "/executions") `
        -Headers $headers
    $customExecutions = @($executions | Where-Object { $_.providerId -ceq "applada-ip-rate-limit" })
}

for ($index = 1; $index -lt $customExecutions.Count; $index++) {
    Invoke-RestMethod `
        -Method Delete `
        -Uri ($base + "/authentication/executions/" + $customExecutions[$index].id) `
        -Headers $headers
}

$customExecutions = @($executions | Where-Object { $_.providerId -ceq "applada-ip-rate-limit" })
if ($customExecutions.Count -eq 0) {
    $executions = Invoke-RestMethod `
        -Method Get `
        -Uri ($base + "/authentication/flows/" + $customAlias + "/executions") `
        -Headers $headers
    $customExecutions = @($executions | Where-Object { $_.providerId -ceq "applada-ip-rate-limit" })
}
if ($customExecutions.Count -ne 1) {
    throw "The Applada IP rate-limit authenticator must appear exactly once."
}

$customExecutions[0].requirement = "REQUIRED"
Invoke-RestMethod `
    -Method Put `
    -Uri ($base + "/authentication/flows/" + $customAlias + "/executions") `
    -Headers $headers `
    -ContentType "application/json" `
    -Body ($customExecutions[0] | ConvertTo-Json -Depth 20)

Invoke-RestMethod `
    -Method Put `
    -Uri $base `
    -Headers $headers `
    -ContentType "application/json" `
    -Body (@{ realm = $Realm; browserFlow = $customAlias } | ConvertTo-Json)

$clients = Invoke-RestMethod -Method Get -Uri ($base + "/clients") -Headers $headers
foreach ($client in $clients) {
    if ($client.directAccessGrantsEnabled) {
        $client.directAccessGrantsEnabled = $false
        Invoke-RestMethod `
            -Method Put `
            -Uri ($base + "/clients/" + $client.id) `
            -Headers $headers `
            -ContentType "application/json" `
            -Body ($client | ConvertTo-Json -Depth 20)
    }
}

Write-Output "Configured '$customAlias' as the $Realm Browser flow."
