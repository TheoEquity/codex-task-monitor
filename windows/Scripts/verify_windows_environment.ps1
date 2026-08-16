[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$codexRoot = Join-Path $env:USERPROFILE '.codex'
$database = Join-Path $codexRoot 'state_5.sqlite'
$sessionIndex = Join-Path $codexRoot 'session_index.jsonl'
$globalState = Join-Path $codexRoot '.codex-global-state.json'
$requiredThreadColumns = @(
    'id', 'rollout_path', 'cwd', 'title', 'archived', 'updated_at_ms',
    'thread_source', 'source', 'preview', 'is_pinned', 'thread_section_id'
)
$requiredSectionColumns = @('id', 'name')
$actualThreadColumns = @()
$actualSectionColumns = @()
$sqlite = Get-Command sqlite3 -ErrorAction SilentlyContinue

if ((Test-Path -LiteralPath $database) -and $null -ne $sqlite) {
    $threadSchema = @(& $sqlite.Source -readonly $database 'PRAGMA table_info(threads);' 2>$null)
    $threadSchemaExitCode = $LASTEXITCODE
    $sectionSchema = @(& $sqlite.Source -readonly $database 'PRAGMA table_info(thread_sections);' 2>$null)
    $sectionSchemaExitCode = $LASTEXITCODE
    if ($threadSchemaExitCode -eq 0 -and $sectionSchemaExitCode -eq 0) {
        $actualThreadColumns = @($threadSchema | ForEach-Object { ($_ -split '\|')[1] })
        $actualSectionColumns = @($sectionSchema | ForEach-Object { ($_ -split '\|')[1] })
    }
}

$protocol = Get-Item -LiteralPath 'Registry::HKEY_CLASSES_ROOT\codex' -ErrorAction SilentlyContinue
try {
    $codexProtocolRegistered = $null -ne $protocol
}
finally {
    if ($null -ne $protocol) { $protocol.Dispose() }
}

$chatGpt = @(Get-Process ChatGPT -ErrorAction SilentlyContinue)
$chatGptMainWindowFound = $false
foreach ($process in $chatGpt) {
    try {
        if ($process.MainWindowHandle -ne 0) { $chatGptMainWindowFound = $true }
    }
    catch {
        # An exiting or inaccessible process is not a visible ChatGPT window.
    }
    finally {
        $process.Dispose()
    }
}
$result = [ordered]@{
    windows_11 = [Environment]::OSVersion.Version.Build -ge 22000
    x64_process = [Environment]::Is64BitProcess
    database_exists = Test-Path -LiteralPath $database
    sqlite_cli_available = $null -ne $sqlite
    session_index_exists = Test-Path -LiteralPath $sessionIndex
    global_state_exists = Test-Path -LiteralPath $globalState
    required_schema_present =
        @($requiredThreadColumns | Where-Object { $_ -notin $actualThreadColumns }).Count -eq 0 -and
        @($requiredSectionColumns | Where-Object { $_ -notin $actualSectionColumns }).Count -eq 0
    codex_protocol_registered = $codexProtocolRegistered
    chatgpt_main_window_found = $chatGptMainWindowFound
}

$result | ConvertTo-Json -Compress
if ($result.Values -contains $false) { exit 1 }
