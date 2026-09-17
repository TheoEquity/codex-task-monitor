$ErrorActionPreference = 'Stop'

. "$PSScriptRoot\..\manage_codex_launch_task.ps1" -ImportOnly

function Assert-Equal([string]$Expected, [string]$Actual, [string]$Name) {
    if ($Expected -cne $Actual) { throw "$Name failed: expected '$Expected', got '$Actual'" }
}

function Assert-True([bool]$Value, [string]$Name) {
    if (-not $Value) { throw "$Name failed" }
}

$applicationDirectory = Join-Path $env:SystemDrive 'Apps\Codex Task Monitor'
$scriptPath = Join-Path $applicationDirectory 'Scripts\manage_codex_launch_task.ps1'
$executablePath = Join-Path $applicationDirectory 'CodexTaskMonitor.exe'
$sid = 'S-1-5-21-1000'
$xmlText = New-CodexLaunchTaskXml -ScriptPath $scriptPath -ExecutablePath $executablePath -UserSid $sid
[xml]$xml = $xmlText
$namespace = [Xml.XmlNamespaceManager]::new($xml.NameTable)
$namespace.AddNamespace('t', 'http://schemas.microsoft.com/windows/2004/02/mit/task')

Assert-Equal $sid $xml.SelectSingleNode('/t:Task/t:Principals/t:Principal/t:UserId', $namespace).InnerText 'task user'
Assert-Equal 'InteractiveToken' $xml.SelectSingleNode('/t:Task/t:Principals/t:Principal/t:LogonType', $namespace).InnerText 'interactive task'
Assert-Equal "\CodexTaskMonitor-OnCodexLaunch-$sid" $xml.SelectSingleNode('/t:Task/t:RegistrationInfo/t:URI', $namespace).InnerText 'per-user task URI'
$subscription = $xml.SelectSingleNode('/t:Task/t:Triggers/t:EventTrigger/t:Subscription', $namespace).InnerText
Assert-True ($subscription.IndexOf('EventID=201', [StringComparison]::Ordinal) -ge 0) 'launch event id'
Assert-True ($subscription.IndexOf("Data[@Name='ApplicationName']='OpenAI.Codex_2p2nqsd0c76g0!App'", [StringComparison]::Ordinal) -ge 0) 'Codex application filter'
$arguments = $xml.SelectSingleNode('/t:Task/t:Actions/t:Exec/t:Arguments', $namespace).InnerText
Assert-True ($arguments.IndexOf('-Mode Launch', [StringComparison]::Ordinal) -ge 0) 'launch mode'
Assert-True ($arguments.IndexOf(('"' + $scriptPath + '"'), [StringComparison]::Ordinal) -ge 0) 'script path'
Assert-True ($arguments.IndexOf(('"' + $executablePath + '"'), [StringComparison]::Ordinal) -ge 0) 'executable path'

$script:registeredTaskName = $null
Register-CodexLaunchTask `
    -ScriptPath $scriptPath `
    -ExecutablePath $executablePath `
    -RegisterTask { param([string]$TaskName, [string]$TaskXml) $script:registeredTaskName = $TaskName }
$currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
Assert-Equal "CodexTaskMonitor-OnCodexLaunch-$currentSid" $script:registeredTaskName 'per-user registration name'

$script:unregisteredTaskName = $null
Unregister-CodexLaunchTask -UnregisterTask { param([string]$TaskName) $script:unregisteredTaskName = $TaskName }
Assert-Equal "CodexTaskMonitor-OnCodexLaunch-$currentSid" $script:unregisteredTaskName 'per-user unregistration name'

$missingRunValue = Read-CodexMonitorRunValue -ValueName "CodexTaskMonitor-Missing-$([Guid]::NewGuid().ToString('N'))"
Assert-True ($null -eq $missingRunValue) 'missing Run value is a successful no-op'

$script:startedPath = $null
$started = Invoke-CodexLaunch `
    -ExecutablePath $executablePath `
    -ReadRunValue { '"' + $executablePath + '"' } `
    -IsRunning { $false } `
    -PathExists { $true } `
    -StartProcess { param([string]$Path) $script:startedPath = $Path }
Assert-True $started 'enabled launch result'
Assert-Equal $executablePath $script:startedPath 'enabled launch path'

$script:startedPath = $null
$started = Invoke-CodexLaunch `
    -ExecutablePath $executablePath `
    -ReadRunValue { $null } `
    -IsRunning { $false } `
    -PathExists { $true } `
    -StartProcess { param([string]$Path) $script:startedPath = $Path }
Assert-True (-not $started) 'disabled launch result'
Assert-True ($null -eq $script:startedPath) 'disabled launch suppression'

$script:startedPath = $null
$started = Invoke-CodexLaunch `
    -ExecutablePath $executablePath `
    -ReadRunValue { '"' + $executablePath + '"' } `
    -IsRunning { $true } `
    -PathExists { $true } `
    -StartProcess { param([string]$Path) $script:startedPath = $Path }
Assert-True (-not $started) 'running launch result'
Assert-True ($null -eq $script:startedPath) 'running launch suppression'

'PASS: Codex launch task'
