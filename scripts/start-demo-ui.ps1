param([switch]$Configure)
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$publicPath = Join-Path $repository '.env.demo-ui.public.json'
$overridePath = Join-Path $repository '.env.demo-ui.compose.json'
$previous = @{}
Push-Location $repository
try {
    $containerId = docker compose ps -q api
    if ($LASTEXITCODE -ne 0 -or !$containerId) { throw 'Start the working API first so its private configuration can be preserved.' }
    $inspection = docker inspect $containerId | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect the current API.' }
    $runtime = @{}
    foreach ($entry in $inspection[0].Config.Env) {
        $pair = $entry -split '=', 2
        if ($pair.Length -eq 2 -and $pair[0].Contains('__')) { $runtime[$pair[0]] = $pair[1] }
    }
    if (!$runtime['Firebase__ProjectId'] -or !$runtime['Firebase__CredentialsJson']) { throw 'Complete Firebase server setup first.' }
    if ((Test-Path -LiteralPath $publicPath) -and !$Configure) {
        $public = Get-Content -LiteralPath $publicPath -Raw | ConvertFrom-Json
        if ($public.projectId -ne $runtime['Firebase__ProjectId']) { throw 'The saved Web config belongs to another project. Remove only .env.demo-ui.public.json and rerun.' }
    }
    else {
        Write-Output 'Enter Firebase Web app settings from Project settings > General. Never use Admin/service-account JSON here.'
        $webKey = Read-Host 'Firebase Web API key (hidden)' -AsSecureString
        $public = [PSCustomObject]@{
            projectId = $runtime['Firebase__ProjectId']
            apiKey = ([System.Net.NetworkCredential]::new('', $webKey)).Password
            messagingSenderId = Read-Host 'Web config messagingSenderId'
            appId = Read-Host 'Web config appId'
            vapidKey = Read-Host 'Web Push certificates PUBLIC key (Enter to configure push later)'
        }
        if (!$public.apiKey -or !$public.appId -or !$public.messagingSenderId) { throw 'The Web API key, appId and messagingSenderId are required.' }
        if (@($public.apiKey,$public.appId,$public.messagingSenderId,$public.vapidKey) | Where-Object { $_ -match 'PRIVATE KEY' }) { throw 'Use public Web app fields, not private credentials.' }
        if ($public.messagingSenderId -notmatch '^\d+$' -or $public.appId -notmatch '^\d+:\d+:web:[A-Za-z0-9]+$') { throw 'Copy messagingSenderId and appId from the Firebase Web app configuration.' }
        [System.IO.File]::WriteAllText($publicPath, ($public | ConvertTo-Json), [System.Text.UTF8Encoding]::new($false))
    }
    # Preserve all current application settings, including email mappings. The generated override stores references only.
    $runtime['Demo__Enabled'] = 'true'
    $runtime['Demo__WebApiKey'] = [string]$public.apiKey
    $runtime['Demo__MessagingSenderId'] = [string]$public.messagingSenderId
    $runtime['Demo__WebAppId'] = [string]$public.appId
    $runtime['Demo__VapidPublicKey'] = [string]$public.vapidKey
    $environment = @{ ASPNETCORE_ENVIRONMENT = 'Demo' }
    $index = 0
    foreach ($name in $runtime.Keys) {
        $temporary = "LABAIK_DEMO_SETTING_$index"
        $previous[$temporary] = [Environment]::GetEnvironmentVariable($temporary, 'Process')
        [Environment]::SetEnvironmentVariable($temporary, $runtime[$name], 'Process')
        $environment[$name] = '${' + $temporary + ':-}'
        $index++
    }
    [System.IO.File]::WriteAllText($overridePath, (@{ services = @{ api = @{ environment = $environment } } } | ConvertTo-Json -Depth 8), [System.Text.UTF8Encoding]::new($false))
    docker compose -f docker-compose.yml -f $overridePath up -d --build --wait --force-recreate api
    if ($LASTEXITCODE -ne 0) { throw 'Demo API startup failed.' }
    Write-Output 'Open http://localhost:8080/demo/ and sign in with your Firebase demo account.'
    Write-Output 'Email, Firebase and other current server settings were preserved. No real email/push receipt is claimed yet.'
}
finally {
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
    Remove-Variable inspection, runtime, webKey -ErrorAction SilentlyContinue
    Pop-Location
}
