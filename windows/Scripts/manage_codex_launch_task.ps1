[CmdletBinding()]
param(
    [ValidateSet('Register', 'Unregister', 'Launch')]
    [string]$Mode = 'Launch',
    [string]$ExecutablePath,
    [switch]$ImportOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$script:CodexLaunchTaskName = 'CodexTaskMonitor-OnCodexLaunch'

function ConvertTo-TaskXmlText([string]$Value) {
    return [Security.SecurityElement]::Escape($Value)
}

function New-CodexLaunchTaskXml(
    [string]$ScriptPath,
    [string]$ExecutablePath,
    [string]$UserSid
) {
    foreach ($value in @($ScriptPath, $ExecutablePath, $UserSid)) {
        if ([string]::IsNullOrWhiteSpace($value)) { throw 'Task registration values cannot be empty.' }
    }

    $powerShellPath = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $arguments = '-NoProfile -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass ' +
        '-File "' + $ScriptPath + '" -Mode Launch -ExecutablePath "' + $ExecutablePath + '"'
    $subscription = "<QueryList><Query Id='0' Path='Microsoft-Windows-AppModel-Runtime/Admin'>" +
        "<Select Path='Microsoft-Windows-AppModel-Runtime/Admin'>" +
        "*[System[Provider[@Name='Microsoft-Windows-AppModel-Runtime'] and EventID=201] and " +
        "EventData[Data[@Name='ApplicationName']='OpenAI.Codex_2p2nqsd0c76g0!App']]" +
        '</Select></Query></QueryList>'

    $escapedSid = ConvertTo-TaskXmlText $UserSid
    $escapedPowerShell = ConvertTo-TaskXmlText $powerShellPath
    $escapedArguments = ConvertTo-TaskXmlText $arguments
    $escapedSubscription = ConvertTo-TaskXmlText $subscription
    return @"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo>
    <Description>Starts Codex Task Monitor when the OpenAI Codex desktop app starts.</Description>
    <URI>\$script:CodexLaunchTaskName</URI>
  </RegistrationInfo>
  <Triggers>
    <EventTrigger>
      <Enabled>true</Enabled>
      <Subscription>$escapedSubscription</Subscription>
    </EventTrigger>
  </Triggers>
  <Principals>
    <Principal id="Author">
      <UserId>$escapedSid</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>LeastPrivilege</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>true</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT1M</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context="Author">
    <Exec>
      <Command>$escapedPowerShell</Command>
      <Arguments>$escapedArguments</Arguments>
    </Exec>
  </Actions>
</Task>
"@
}

function Register-CodexLaunchTask(
    [string]$ScriptPath,
    [string]$ExecutablePath,
    [scriptblock]$RegisterTask
) {
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $xml = New-CodexLaunchTaskXml -ScriptPath $ScriptPath -ExecutablePath $ExecutablePath -UserSid $sid
    if ($null -eq $RegisterTask) {
        $RegisterTask = {
            param([string]$TaskName, [string]$TaskXml)
            Register-ScheduledTask -TaskName $TaskName -Xml $TaskXml -Force | Out-Null
        }
    }

    & $RegisterTask $script:CodexLaunchTaskName $xml
}

function Unregister-CodexLaunchTask([scriptblock]$UnregisterTask) {
    if ($null -eq $UnregisterTask) {
        $UnregisterTask = {
            param([string]$TaskName)
            Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
        }
    }

    & $UnregisterTask $script:CodexLaunchTaskName
}

function ConvertTo-NormalizedPath([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    try {
        return [IO.Path]::GetFullPath($Value.Trim().Trim('"'))
    }
    catch {
        return $null
    }
}

function Invoke-CodexLaunch(
    [string]$ExecutablePath,
    [scriptblock]$ReadRunValue,
    [scriptblock]$IsRunning,
    [scriptblock]$PathExists,
    [scriptblock]$StartProcess
) {
    if ([string]::IsNullOrWhiteSpace($ExecutablePath)) { throw 'ExecutablePath is required.' }
    if ($null -eq $ReadRunValue) {
        $ReadRunValue = {
            (Get-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' `
                -Name CodexTaskMonitor -ErrorAction SilentlyContinue).CodexTaskMonitor
        }
    }
    if ($null -eq $IsRunning) {
        $IsRunning = {
            param([string]$Path)
            $name = [IO.Path]::GetFileNameWithoutExtension($Path)
            foreach ($process in @(Get-Process -Name $name -ErrorAction SilentlyContinue)) {
                try {
                    if ([string]::Equals($process.Path, $Path, [StringComparison]::OrdinalIgnoreCase)) { return $true }
                }
                catch {
                }
                finally {
                    $process.Dispose()
                }
            }
            return $false
        }
    }
    if ($null -eq $PathExists) {
        $PathExists = { param([string]$Path) Test-Path -LiteralPath $Path -PathType Leaf }
    }
    if ($null -eq $StartProcess) {
        $StartProcess = { param([string]$Path) Start-Process -FilePath $Path | Out-Null }
    }

    $normalizedExecutable = ConvertTo-NormalizedPath $ExecutablePath
    $normalizedRunTarget = ConvertTo-NormalizedPath (& $ReadRunValue)
    if ($null -eq $normalizedExecutable -or
        -not [string]::Equals($normalizedExecutable, $normalizedRunTarget, [StringComparison]::OrdinalIgnoreCase) -or
        -not (& $PathExists $normalizedExecutable) -or
        (& $IsRunning $normalizedExecutable)) {
        return $false
    }

    & $StartProcess $normalizedExecutable
    return $true
}

if (-not $ImportOnly) {
    switch ($Mode) {
        'Register' {
            if ([string]::IsNullOrWhiteSpace($ExecutablePath)) { throw 'ExecutablePath is required.' }
            Register-CodexLaunchTask -ScriptPath $PSCommandPath -ExecutablePath $ExecutablePath
        }
        'Unregister' {
            Unregister-CodexLaunchTask
        }
        'Launch' {
            [void](Invoke-CodexLaunch -ExecutablePath $ExecutablePath)
        }
    }
}
