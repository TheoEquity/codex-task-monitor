$ErrorActionPreference = 'Stop'

. "$PSScriptRoot\..\provision_bark.ps1" -ImportOnly

function Assert-Equal([string]$Expected, [string]$Actual, [string]$Name) {
    if ($Expected -cne $Actual) {
        throw "$Name failed"
    }
}

function Assert-Throws([scriptblock]$Action, [string]$Name) {
    try {
        & $Action
    }
    catch {
        return
    }

    throw "$Name failed"
}

function Assert-True([bool]$Value, [string]$Name) {
    if (-not $Value) {
        throw "$Name failed"
    }
}

Assert-Equal 'https://example.invalid/fake-device-key' `
    (ConvertTo-BarkEndpoint 'https://example.invalid/fake-device-key/Body Text').AbsoluteUri `
    'body sample'

Assert-Equal 'https://example.invalid/fake-device-key' `
    (ConvertTo-BarkEndpoint 'https://example.invalid/fake-device-key/Title/Body Text').AbsoluteUri `
    'title body sample'

Assert-Throws { ConvertTo-BarkEndpoint 'http://example.invalid/fake-device-key/Body Text' } 'http rejection'
Assert-Throws { ConvertTo-BarkEndpoint 'https://example.invalid/fake-device-key/unknown' } 'unknown path rejection'

$script:capturedEndpoint = $null
$script:capturedPayload = $null
$successRequest = {
    param([Uri]$Endpoint, [string]$Payload)
    $script:capturedEndpoint = $Endpoint
    $script:capturedPayload = $Payload
    [pscustomobject]@{ code = 200; message = 'success'; timestamp = 1786996800 }
}

Send-BarkTest ([Uri]'https://example.invalid/fake-device-key') $successRequest
$payload = $script:capturedPayload | ConvertFrom-Json
Assert-Equal 'https://example.invalid/fake-device-key' $script:capturedEndpoint.AbsoluteUri 'test endpoint'
Assert-Equal 'Codex Task Monitor' $payload.title 'test title'
Assert-Equal 'Bark 连接成功' $payload.body 'test body'
Assert-Equal 'Codex Task Monitor' $payload.group 'test group'
Assert-Throws {
    Send-BarkTest ([Uri]'https://example.invalid/fake-device-key') {
        param([Uri]$Endpoint, [string]$Payload)
        [pscustomobject]@{ code = 400; message = 'private'; timestamp = 1786996800 }
    }
} 'Bark rejection'

$root = Join-Path ([IO.Path]::GetTempPath()) "bark-provision-$([Guid]::NewGuid().ToString('N'))"
try {
    Invoke-BarkProvisioning `
        -ReadClipboard { 'https://example.invalid/fake-device-key/Body Text' } `
        -FindProcess { $null } `
        -Request $successRequest `
        -DestinationDirectory $root | Out-Null

    $statePath = Join-Path $root 'bark-state.json'
    $secretPath = Join-Path $root 'bark-secret.dat'
    Assert-True ([IO.File]::Exists($statePath)) 'state file'
    Assert-True ([IO.File]::Exists($secretPath)) 'secret file'
    $stateText = [IO.File]::ReadAllText($statePath)
    Assert-True (-not $stateText.Contains('fake-device-key', [StringComparison]::Ordinal)) 'state secrecy'
    $state = $stateText | ConvertFrom-Json
    Assert-True $state.enabled 'enabled state'
    Assert-Equal '0' ([string]$state.notifiedItemIds.Count) 'empty notified ids'

    [byte[]]$clearBytes = [Security.Cryptography.ProtectedData]::Unprotect(
        [IO.File]::ReadAllBytes($secretPath),
        $null,
        [Security.Cryptography.DataProtectionScope]::CurrentUser)
    try {
        $secret = [Text.Encoding]::UTF8.GetString($clearBytes) | ConvertFrom-Json
        Assert-Equal ([string]$state.configurationId) ([string]$secret.configurationId) 'configuration identity'
        Assert-Equal 'https://example.invalid/fake-device-key' $secret.endpoint 'protected endpoint'
    }
    finally {
        [Security.Cryptography.CryptographicOperations]::ZeroMemory($clearBytes)
    }
}
finally {
    if ([IO.Directory]::Exists($root)) {
        [IO.Directory]::Delete($root, $true)
    }
}

Assert-Throws {
    Invoke-BarkProvisioning `
        -ReadClipboard { 'https://example.invalid/fake-device-key/Body Text' } `
        -FindProcess { [pscustomobject]@{ Id = 1 } } `
        -Request $successRequest `
        -DestinationDirectory $root
} 'running process rejection'

'PASS: Bark provisioning'
