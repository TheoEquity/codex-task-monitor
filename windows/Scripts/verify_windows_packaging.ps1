[CmdletBinding()]
param(
    [switch]$RequireOutputs
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

function Assert-Condition {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) { throw $Message }
}

function Assert-Contains {
    param(
        [string]$Text,
        [string]$Expected,
        [string]$Path
    )

    Assert-Condition ($Text.Contains($Expected)) "Expected '$Path' to contain '$Expected'."
}

$projectPath = Join-Path $repositoryRoot 'windows\CodexTaskMonitor.Windows\CodexTaskMonitor.Windows.csproj'
[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$properties = @{}
foreach ($propertyGroup in $project.Project.PropertyGroup) {
    foreach ($property in $propertyGroup.ChildNodes) {
        if ($property.NodeType -eq [System.Xml.XmlNodeType]::Element) {
            $properties[$property.Name] = $property.InnerText
        }
    }
}

$expectedProperties = [ordered]@{
    Version = '1.0.0'
    RuntimeIdentifier = 'win-x64'
    SelfContained = 'true'
    PublishSingleFile = 'true'
    IncludeNativeLibrariesForSelfExtract = 'true'
    DebugType = 'embedded'
}
foreach ($expectedProperty in $expectedProperties.GetEnumerator()) {
    Assert-Condition ($properties[$expectedProperty.Key] -eq $expectedProperty.Value) "Expected $projectPath to set $($expectedProperty.Key) to $($expectedProperty.Value)."
}

$installerPath = Join-Path $repositoryRoot 'windows\Installer\CodexTaskMonitor.iss'
Assert-Condition (Test-Path -LiteralPath $installerPath) "Missing installer definition: $installerPath"
$installer = Get-Content -LiteralPath $installerPath -Raw
foreach ($expectedValue in @(
    'PrivilegesRequired=lowest',
    'DefaultDirName={localappdata}\Programs\Codex Task Monitor',
    'ArchitecturesAllowed=x64compatible',
    'ArchitecturesInstallIn64BitMode=x64compatible',
    'Source: "..\publish\win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs',
    'Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"',
    'Flags: uninsdeletevalue',
    "RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'CodexTaskMonitor');"
)) {
    Assert-Contains $installer $expectedValue $installerPath
}

$workflowPath = Join-Path $repositoryRoot '.github\workflows\windows.yml'
Assert-Condition (Test-Path -LiteralPath $workflowPath) "Missing workflow: $workflowPath"
$workflow = Get-Content -LiteralPath $workflowPath -Raw
Assert-Condition (-not $workflow.Contains('run: "& ')) 'Workflow commands containing Windows paths must use a YAML single-quoted scalar.'
foreach ($expectedValue in @(
    'dotnet test windows/CodexTaskMonitor.sln -c Release --no-restore',
    'dotnet publish windows/CodexTaskMonitor.Windows/CodexTaskMonitor.Windows.csproj -c Release -r win-x64 --self-contained true --no-restore -o windows/publish/win-x64',
    "C:\Program Files\Inno Setup 7\ISCC.exe",
    'actions/upload-artifact@v4',
    "startsWith(github.ref, 'refs/tags/v')",
    'gh release upload $tag windows/artifacts/Codex-Task-Monitor-Windows-x64-Setup.exe --clobber'
)) {
    Assert-Contains $workflow $expectedValue $workflowPath
}

foreach ($generatedPath in @('windows/publish/win-x64/CodexTaskMonitor.exe', 'windows/artifacts/Codex-Task-Monitor-Windows-x64-Setup.exe')) {
    git -C $repositoryRoot check-ignore -q -- $generatedPath
    Assert-Condition ($LASTEXITCODE -eq 0) "Generated packaging output is not ignored: $generatedPath"
}

if ($RequireOutputs) {
    $publishDirectory = Join-Path $repositoryRoot 'windows\publish\win-x64'
    $appPath = Join-Path $publishDirectory 'CodexTaskMonitor.exe'
    $installerOutputPath = Join-Path $repositoryRoot 'windows\artifacts\Codex-Task-Monitor-Windows-x64-Setup.exe'
    Assert-Condition (Test-Path -LiteralPath $appPath -PathType Leaf) "Missing self-contained application: $appPath"
    Assert-Condition ((Get-Item -LiteralPath $appPath).Length -gt 0) "Published application is empty: $appPath"
    Assert-Condition ([System.IO.File]::ReadAllBytes($appPath)[0] -eq 0x4D -and [System.IO.File]::ReadAllBytes($appPath)[1] -eq 0x5A) "Published application is not a Windows executable: $appPath"
    Assert-Condition (Test-Path -LiteralPath $installerOutputPath -PathType Leaf) "Missing installer: $installerOutputPath"
    Assert-Condition ((Get-Item -LiteralPath $installerOutputPath).Length -gt 0) "Installer is empty: $installerOutputPath"
}

Write-Output 'Windows packaging configuration is valid.'
