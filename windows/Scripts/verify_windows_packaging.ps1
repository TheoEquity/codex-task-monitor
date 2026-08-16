[CmdletBinding()]
param(
    [switch]$RequireOutputs,
    [string]$ExpectedInstallerSha256
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

function Get-ObjectProperty {
    param(
        [object]$Object,
        [string]$Name
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Find-WorkflowStep {
    param(
        [object[]]$Steps,
        [string]$Property,
        [string]$ExpectedValue
    )

    return @($Steps | Where-Object { (Get-ObjectProperty $_ $Property) -eq $ExpectedValue })
}

function Assert-WindowsExecutable {
    param(
        [string]$Path,
        [string]$Description
    )

    Assert-Condition (Test-Path -LiteralPath $Path -PathType Leaf) "Missing ${Description}: $Path"
    Assert-Condition ((Get-Item -LiteralPath $Path).Length -gt 0) "$Description is empty: $Path"
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $header = New-Object byte[] 2
        $bytesRead = $stream.Read($header, 0, $header.Length)
    }
    finally {
        $stream.Dispose()
    }
    Assert-Condition ($bytesRead -eq 2 -and $header[0] -eq 0x4D -and $header[1] -eq 0x5A) "$Description is not a Windows executable: $Path"
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
$converter = Get-Command ConvertFrom-Yaml -ErrorAction SilentlyContinue
if ($null -ne $converter) {
    $workflowObject = $workflow | & $converter.Source
}
else {
    $python = Get-Command python -ErrorAction SilentlyContinue
    Assert-Condition ($null -ne $python) 'ConvertFrom-Yaml or Python with PyYAML is required to validate the workflow structure.'
    $workflowJson = $workflow | & $python.Source -c 'import json, sys, yaml; print(json.dumps(yaml.safe_load(sys.stdin.read())))'
    Assert-Condition ($LASTEXITCODE -eq 0) 'Python/PyYAML could not parse the GitHub Actions workflow.'
    $workflowObject = $workflowJson | ConvertFrom-Json
}

Assert-Condition ((Get-ObjectProperty (Get-ObjectProperty $workflowObject 'permissions') 'contents') -eq 'read') 'Workflow defaults must grant only contents: read.'
$jobs = Get-ObjectProperty $workflowObject 'jobs'
$packageJob = Get-ObjectProperty $jobs 'build-test-package'
Assert-Condition ($null -ne $packageJob) 'Workflow must define the build-test-package job.'
Assert-Condition ((Get-ObjectProperty $packageJob 'runs-on') -eq 'windows-latest') 'Packaging job must run on windows-latest.'
Assert-Condition ((Get-ObjectProperty (Get-ObjectProperty $packageJob 'permissions') 'contents') -eq 'read') 'Packaging job must grant only contents: read.'
$steps = @(Get-ObjectProperty $packageJob 'steps')

$checkout = @(Find-WorkflowStep $steps 'uses' 'actions/checkout@v4')
Assert-Condition ($checkout.Count -eq 1) 'Workflow must check out source with actions/checkout@v4.'
$setupDotnet = @(Find-WorkflowStep $steps 'uses' 'actions/setup-dotnet@v4')
Assert-Condition ($setupDotnet.Count -eq 1) 'Workflow must install .NET with actions/setup-dotnet@v4.'
$setupDotnetWith = Get-ObjectProperty $setupDotnet[0] 'with'
Assert-Condition ((Get-ObjectProperty $setupDotnetWith 'dotnet-version') -eq '8.0.424') 'Workflow must pin the .NET SDK to 8.0.424.'
$cacheValue = Get-ObjectProperty $setupDotnetWith 'cache'
if ("$cacheValue" -ieq 'true') {
    $cacheDependencyPath = Get-ObjectProperty $setupDotnetWith 'cache-dependency-path'
    Assert-Condition (-not [string]::IsNullOrWhiteSpace("$cacheDependencyPath")) 'setup-dotnet cache requires cache-dependency-path entries for packages.lock.json files.'
    $cacheInputs = @("$cacheDependencyPath" -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    $resolvedCacheInputs = @()
    foreach ($cacheInput in $cacheInputs) {
        $resolvedCacheInputs += @(Get-ChildItem -Path (Join-Path $repositoryRoot $cacheInput) -File -ErrorAction SilentlyContinue)
    }
    Assert-Condition ($resolvedCacheInputs.Count -gt 0 -and @($resolvedCacheInputs | Where-Object { $_.Name -ne 'packages.lock.json' }).Count -eq 0) 'setup-dotnet cache may only target existing NuGet packages.lock.json files.'
}

foreach ($expectedRun in @(
    'dotnet restore windows/CodexTaskMonitor.sln',
    'dotnet test windows/CodexTaskMonitor.sln -c Release --no-restore --logger "trx;LogFileName=windows-tests.trx"',
    'dotnet publish windows/CodexTaskMonitor.Windows/CodexTaskMonitor.Windows.csproj -c Release -r win-x64 --self-contained true --no-restore -o windows/publish/win-x64',
    'winget install --exact --id JRSoftware.InnoSetup.7 --version 7.1.0 --source winget --silent --accept-source-agreements --accept-package-agreements',
    "& 'C:\Program Files\Inno Setup 7\ISCC.exe' windows/Installer/CodexTaskMonitor.iss"
)) {
    Assert-Condition (@(Find-WorkflowStep $steps 'run' $expectedRun).Count -eq 1) "Workflow is missing the required command: $expectedRun"
}

$artifactStep = @(Find-WorkflowStep $steps 'uses' 'actions/upload-artifact@v4')
Assert-Condition ($artifactStep.Count -eq 1) 'Workflow must upload the packaged Windows outputs.'
$artifactPaths = @(("$(Get-ObjectProperty (Get-ObjectProperty $artifactStep[0] 'with') 'path')" -split "`r?`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ }))
foreach ($artifactPath in @('windows/publish/win-x64/**', 'windows/artifacts/Codex-Task-Monitor-Windows-x64-Setup.exe', 'windows/CodexTaskMonitor.Tests/TestResults/**')) {
    Assert-Condition ($artifactPaths -contains $artifactPath) "Workflow artifact upload is missing: $artifactPath"
}

$releaseStepsInPackageJob = @(Find-WorkflowStep $steps 'name' 'Create or update tag release')
Assert-Condition ($releaseStepsInPackageJob.Count -eq 0) 'Packaging must not receive release-write capability.'
$releaseJob = Get-ObjectProperty $jobs 'release'
Assert-Condition ($null -ne $releaseJob) 'Workflow must define a separate release job.'
Assert-Condition ((Get-ObjectProperty $releaseJob 'needs') -eq 'build-test-package') 'Release job must wait for packaging.'
Assert-Condition ((Get-ObjectProperty $releaseJob 'if') -eq "startsWith(github.ref, 'refs/tags/v')") 'Release job must only run for v* tags.'
Assert-Condition ((Get-ObjectProperty (Get-ObjectProperty $releaseJob 'permissions') 'contents') -eq 'write') 'Only the release job may grant contents: write.'
$releaseSteps = @(Get-ObjectProperty $releaseJob 'steps')
$download = @(Find-WorkflowStep $releaseSteps 'uses' 'actions/download-artifact@v4')
Assert-Condition ($download.Count -eq 1) 'Release job must download the packaged artifact.'
$downloadWith = Get-ObjectProperty $download[0] 'with'
Assert-Condition ((Get-ObjectProperty $downloadWith 'name') -eq 'Codex-Task-Monitor-Windows-x64') 'Release job must download the Windows package artifact.'
Assert-Condition ((Get-ObjectProperty $downloadWith 'path') -eq 'windows/artifacts') 'Release job must download the installer to its upload path.'
$releaseStep = @(Find-WorkflowStep $releaseSteps 'name' 'Create or update tag release')
Assert-Condition ($releaseStep.Count -eq 1) 'Workflow must include the tag release upload step.'
Assert-Contains (Get-ObjectProperty $releaseStep[0] 'run') 'gh release upload $tag windows/artifacts/Codex-Task-Monitor-Windows-x64-Setup.exe --clobber' $workflowPath

foreach ($generatedPath in @('windows/publish/win-x64/CodexTaskMonitor.exe', 'windows/artifacts/Codex-Task-Monitor-Windows-x64-Setup.exe')) {
    git -C $repositoryRoot check-ignore -q -- $generatedPath
    Assert-Condition ($LASTEXITCODE -eq 0) "Generated packaging output is not ignored: $generatedPath"
}

if ($RequireOutputs) {
    $publishDirectory = Join-Path $repositoryRoot 'windows\publish\win-x64'
    $appPath = Join-Path $publishDirectory 'CodexTaskMonitor.exe'
    $installerOutputPath = Join-Path $repositoryRoot 'windows\artifacts\Codex-Task-Monitor-Windows-x64-Setup.exe'
    Assert-WindowsExecutable $appPath 'self-contained application'
    $publishFiles = @(Get-ChildItem -LiteralPath $publishDirectory -File)
    Assert-Condition ($publishFiles.Count -eq 1 -and $publishFiles[0].Name -eq 'CodexTaskMonitor.exe') 'Self-contained publish directory must contain only CodexTaskMonitor.exe.'
    Assert-Condition (@($publishFiles | Where-Object { $_.Extension -eq '.dll' -or $_.Name -match '\.(runtimeconfig|deps)\.json$' }).Count -eq 0) 'Self-contained publish directory must not contain managed runtime dependencies.'
    Assert-Condition ((Get-AuthenticodeSignature -LiteralPath $appPath).Status -eq 'NotSigned') 'First release application must be unsigned.'
    Assert-WindowsExecutable $installerOutputPath 'installer'
    Assert-Condition ((Get-AuthenticodeSignature -LiteralPath $installerOutputPath).Status -eq 'NotSigned') 'First release installer must be unsigned.'
    $installerHash = (Get-FileHash -LiteralPath $installerOutputPath -Algorithm SHA256).Hash
    Assert-Condition ($installerHash -match '^[A-F0-9]{64}$') 'Installer SHA-256 must be a 64-character hexadecimal digest.'
    if (-not [string]::IsNullOrWhiteSpace($ExpectedInstallerSha256)) {
        Assert-Condition ($installerHash -eq $ExpectedInstallerSha256.ToUpperInvariant()) 'Installer SHA-256 does not match ExpectedInstallerSha256.'
    }
    Write-Output "InstallerSHA256=$installerHash"
}

Write-Output 'Windows packaging configuration is valid.'
