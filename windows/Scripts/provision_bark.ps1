[CmdletBinding()]
param([switch]$ImportOnly)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function ConvertTo-BarkEndpoint([string]$InputUrl) {
    try {
        $uri = [Uri]::new($InputUrl.Trim(), [UriKind]::Absolute)
    }
    catch {
        throw 'Bark address is invalid.'
    }

    if ($uri.Scheme -cne 'https' -or
        -not [string]::IsNullOrEmpty($uri.UserInfo) -or
        -not [string]::IsNullOrEmpty($uri.Fragment) -or
        -not [string]::IsNullOrEmpty($uri.Query)) {
        throw 'Bark address is invalid.'
    }

    $segments = @($uri.AbsolutePath.Trim('/').Split('/', [StringSplitOptions]::RemoveEmptyEntries) |
        ForEach-Object { [Uri]::UnescapeDataString($_) })
    if ($segments.Count -lt 1 -or [string]::IsNullOrWhiteSpace($segments[0])) {
        throw 'Bark address is invalid.'
    }

    $suffix = @()
    if ($segments.Count -gt 1) {
        $suffix = @($segments[1..($segments.Count - 1)])
    }
    $recognized =
        $suffix.Count -eq 0 -or
        ($suffix.Count -eq 1 -and $suffix[0] -ceq 'Body Text') -or
        ($suffix.Count -eq 2 -and $suffix[0] -ceq 'Title' -and $suffix[1] -ceq 'Body Text') -or
        ($suffix.Count -eq 3 -and $suffix[0] -ceq 'Title' -and $suffix[1] -ceq 'Subtitle' -and $suffix[2] -ceq 'Body Text')
    if (-not $recognized) {
        throw 'Bark sample path is not recognized.'
    }

    $builder = [UriBuilder]::new($uri)
    $builder.Path = '/' + [Uri]::EscapeDataString($segments[0])
    $builder.Query = ''
    $builder.Fragment = ''
    return $builder.Uri
}

function Send-BarkTest([Uri]$Endpoint, [scriptblock]$Request) {
    $payload = [ordered]@{
        title = 'Codex Task Monitor'
        body = 'Bark 连接成功'
        group = 'Codex Task Monitor'
    } | ConvertTo-Json -Compress

    try {
        $response = & $Request $Endpoint $payload
        if ($response.code -ne 200) {
            throw 'rejected'
        }
    }
    catch {
        throw 'Bark test notification failed.'
    }
}

function Protect-BarkSecret([Guid]$ConfigurationId, [Uri]$Endpoint) {
    $document = [ordered]@{
        configurationId = $ConfigurationId
        endpoint = $Endpoint.AbsoluteUri
    } | ConvertTo-Json -Compress
    [byte[]]$clearBytes = [Text.Encoding]::UTF8.GetBytes($document)
    try {
        [byte[]]$protectedBytes = [Security.Cryptography.ProtectedData]::Protect(
            $clearBytes,
            $null,
            [Security.Cryptography.DataProtectionScope]::CurrentUser)
        return ,$protectedBytes
    }
    finally {
        [Security.Cryptography.CryptographicOperations]::ZeroMemory($clearBytes)
    }
}

function Write-BarkState(
    [string]$Path,
    [Guid]$ConfigurationId,
    [DateTimeOffset]$EnabledAt) {
    $document = [ordered]@{
        enabled = $true
        configurationId = $ConfigurationId
        enabledAt = $EnabledAt.ToUniversalTime().ToString('O')
        notifiedItemIds = @()
    } | ConvertTo-Json
    [IO.File]::WriteAllText($Path, $document, [Text.UTF8Encoding]::new($false))
}

function Invoke-BarkProvisioning {
    param(
        [scriptblock]$ReadClipboard = { Get-Clipboard -Raw },
        [scriptblock]$FindProcess = { Get-Process -Name 'CodexTaskMonitor' -ErrorAction SilentlyContinue },
        [scriptblock]$Request = {
            param([Uri]$Endpoint, [string]$Payload)
            Invoke-RestMethod -Method Post -Uri $Endpoint -ContentType 'application/json; charset=utf-8' `
                -Body $Payload -TimeoutSec 10
        },
        [string]$DestinationDirectory = (Join-Path $env:LOCALAPPDATA 'CodexTaskMonitor')
    )

    $runningProcess = & $FindProcess
    if ($null -ne $runningProcess) {
        throw 'Exit Codex Task Monitor before provisioning Bark.'
    }

    $clipboardText = & $ReadClipboard
    if ([string]::IsNullOrWhiteSpace($clipboardText)) {
        throw 'The clipboard does not contain a Bark address.'
    }

    $endpoint = ConvertTo-BarkEndpoint $clipboardText
    Send-BarkTest $endpoint $Request

    $configurationId = [Guid]::NewGuid()
    $enabledAt = [DateTimeOffset]::UtcNow
    [byte[]]$protectedSecret = Protect-BarkSecret $configurationId $endpoint
    [IO.Directory]::CreateDirectory($DestinationDirectory) | Out-Null
    $secretPath = Join-Path $DestinationDirectory 'bark-secret.dat'
    $statePath = Join-Path $DestinationDirectory 'bark-state.json'
    $secretTemporary = "$secretPath.$([Guid]::NewGuid().ToString('N')).tmp"
    $stateTemporary = "$statePath.$([Guid]::NewGuid().ToString('N')).tmp"

    try {
        [IO.File]::WriteAllBytes($secretTemporary, $protectedSecret)
        Write-BarkState $stateTemporary $configurationId $enabledAt
        [IO.File]::Move($secretTemporary, $secretPath, $true)
        [IO.File]::Move($stateTemporary, $statePath, $true)
    }
    finally {
        [Security.Cryptography.CryptographicOperations]::ZeroMemory($protectedSecret)
        if ([IO.File]::Exists($secretTemporary)) { [IO.File]::Delete($secretTemporary) }
        if ([IO.File]::Exists($stateTemporary)) { [IO.File]::Delete($stateTemporary) }
    }

    'Bark test succeeded and the endpoint was stored securely.'
}

if (-not $ImportOnly) {
    Invoke-BarkProvisioning
}
