#requires -Version 7.0
param([ValidateSet('Enable', 'Disable', 'Status')][string]$Action = 'Status')
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$OutputEncoding = [Text.UTF8Encoding]::new($false)
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$accountSid = $identity.User.Value
$taskName = "SamaLite-$accountSid"
$description = 'SamaLite portable startup: current user, interactive tray, highest privileges.'
$executable = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'SamaLite.exe'))
$scheduler = New-Object -ComObject 'Schedule.Service'
$scheduler.Connect()
$folder = $scheduler.GetFolder('\')
$existing = $folder.GetTasks(0) | Where-Object Name -eq $taskName | Select-Object -First 1
if ($existing -and $existing.Definition.RegistrationInfo.Description -ne $description) {
    throw "Task $taskName is not owned by this script; left unchanged."
}
if ($Action -ne 'Status') {
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this script from PowerShell 7 with administrator privileges to change startup.'
    }
}
if ($Action -eq 'Enable') {
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) { throw 'Run the packaged copy of Startup.ps1 beside SamaLite.exe.' }
    $definition = $scheduler.NewTask(0)
    $definition.RegistrationInfo.Description = $description
    $definition.RegistrationInfo.Author = $identity.Name
    $definition.Principal.UserId = $accountSid
    $definition.Principal.LogonType = 3 # InteractiveToken: tray belongs to the logged-on user, no password stored.
    $definition.Principal.RunLevel = 1 # Highest: required by the existing PawnIO temperature source.
    $definition.Settings.Enabled = $true
    $definition.Settings.AllowDemandStart = $true
    $definition.Settings.StartWhenAvailable = $true
    $definition.Settings.DisallowStartIfOnBatteries = $false
    $definition.Settings.StopIfGoingOnBatteries = $false
    $definition.Settings.ExecutionTimeLimit = 'PT0S'
    $definition.Settings.MultipleInstances = 2 # IgnoreNew
    $definition.Settings.RestartInterval = 'PT1M'
    $definition.Settings.RestartCount = 3
    $definition.Settings.Priority = 7
    $trigger = $definition.Triggers.Create(9) # Logon
    $trigger.UserId = $accountSid
    $trigger.Delay = 'PT15S'
    $trigger.Enabled = $true
    $exec = $definition.Actions.Create(0)
    $exec.Path = $executable
    $exec.Arguments = '--startup'
    $exec.WorkingDirectory = $PSScriptRoot
    $existing = $folder.RegisterTaskDefinition($taskName, $definition, 6, $accountSid, $null, 3, $null)
}
if ($Action -eq 'Disable' -and $existing) { $folder.DeleteTask($taskName, 0); $existing = $null }
$registeredSid = $null
if ($existing) {
    $registeredUser = $existing.Definition.Principal.UserId
    $registeredSid = if ($registeredUser.StartsWith('S-1-', [StringComparison]::OrdinalIgnoreCase)) { $registeredUser }
        else { [Security.Principal.NTAccount]::new($registeredUser).Translate([Security.Principal.SecurityIdentifier]).Value }
}
[ordered]@{ Name = $taskName; Enabled = [bool]($existing -and $existing.Enabled)
    Executable = if ($existing) { $existing.Definition.Actions.Item(1).Path } else { $null }
    Arguments = if ($existing) { $existing.Definition.Actions.Item(1).Arguments } else { $null }
    HighestPrivileges = [bool]($existing -and $existing.Definition.Principal.RunLevel -eq 1)
    CurrentUserOnly = [bool]($existing -and $registeredSid -eq $accountSid)
    Delay = if ($existing) { $existing.Definition.Triggers.Item(1).Delay } else { $null }
} | ConvertTo-Json
