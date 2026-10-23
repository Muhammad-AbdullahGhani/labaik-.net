param(
    [Parameter(Mandatory = $true)]
    [string]$AccessToken
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
Push-Location $repository
$previous = @{}
$names = @('EMAIL_API_KEY', 'EMAIL_FROM', 'DEMO_EMAIL_RECIPIENT', 'FIREBASE_PROJECT_ID', 'FIREBASE_CREDENTIALS_JSON')
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    # Resolve the mapping from a verified local session, never from a client-supplied role or email identity.
    $me = Invoke-RestMethod 'http://localhost:8080/auth/me' -Headers @{ Authorization = "Bearer $AccessToken" } -ErrorAction Stop
    $userId = [Guid]::Parse($me.userId).ToString()
    $recipient = Read-Host 'Email address associated with your Resend account (test recipient)'
    $address = [System.Net.Mail.MailAddress]::new($recipient)
    if ($address.Address -ne $recipient -or $recipient.Contains("`r") -or $recipient.Contains("`n")) { throw 'Enter one plain email address.' }
    $secret = Read-Host 'Resend sending API key (hidden)' -AsSecureString
    $env:EMAIL_API_KEY = ([System.Net.NetworkCredential]::new('', $secret)).Password
    if ([string]::IsNullOrWhiteSpace($env:EMAIL_API_KEY)) { throw 'A Resend sending key is required.' }
    $env:EMAIL_FROM = 'onboarding@resend.dev'
    $env:DEMO_EMAIL_RECIPIENT = $recipient

    # Recreating from a new terminal must not accidentally remove the working Firebase configuration.
    $containerId = docker compose ps -q api
    if ($LASTEXITCODE -ne 0 -or !$containerId) { throw 'Start the API first.' }
    $inspection = docker inspect $containerId | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the running API configuration.' }
    $runtime = @{}
    foreach ($entry in $inspection[0].Config.Env) {
        $pair = $entry -split '=', 2
        if ($pair.Length -eq 2) { $runtime[$pair[0]] = $pair[1] }
    }
    if (!$env:FIREBASE_PROJECT_ID) { $env:FIREBASE_PROJECT_ID = $runtime['Firebase__ProjectId'] }
    if (!$env:FIREBASE_CREDENTIALS_JSON) { $env:FIREBASE_CREDENTIALS_JSON = $runtime['Firebase__CredentialsJson'] }
    if (!$env:FIREBASE_PROJECT_ID -or !$env:FIREBASE_CREDENTIALS_JSON) { throw 'Load your Firebase settings first.' }

    # This ignored override contains environment references, not the provider key or recipient address.
    $environment = @{}
    $environment["Notifications__EmailRecipients__$userId"] = '${DEMO_EMAIL_RECIPIENT:?Run configure-demo-email.ps1}'
    $override = @{ services = @{ api = @{ environment = $environment } } }
    $overridePath = Join-Path $repository '.env.demo-email.compose.json'
    $json = $override | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($overridePath, $json, [System.Text.UTF8Encoding]::new($false))
    docker compose -f docker-compose.yml -f $overridePath up -d --build --wait --force-recreate api
    if ($LASTEXITCODE -ne 0) { throw 'API recreation failed; email delivery is not verified.' }
    Write-Output 'Email mapping configured for the authenticated demo account. No email has been sent yet.'
    Write-Output 'Next: register the push recipient, then generate a fresh booking/payment event through the outbox.'
}
finally {
    # Secrets stay in the container environment; do not leave extra copies in this shell or an exported collection.
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
    Remove-Variable secret, inspection, runtime -ErrorAction SilentlyContinue
    Pop-Location
}
