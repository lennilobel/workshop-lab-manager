# 05-Start-Student-VMs.ps1

# Fail on any error
$ErrorActionPreference = 'Stop'

# --- config -------------------------------------------------
$RG          = "sql-hol-vslive-rg"
$NAME_PREFIX = "vm-sql-hol-attendee-"
# ------------------------------------------------------------

function Invoke-AzCli {
    param([Parameter(Mandatory)] [string[]] $Args, [string] $OkMessage = "OK")
    & az @Args
    if ($LASTEXITCODE -ne 0) { throw "Azure CLI failed: az $($Args -join ' ')" }
    if ($OkMessage) { Write-Host "[OK] $OkMessage" }
}

# Pretty time: hh:mm:ss.mmm
function Format-Elapsed {
    param([TimeSpan]$ts)
    "{0:00}:{1:00}:{2:00}.{3:000}" -f $ts.Hours, $ts.Minutes, $ts.Seconds, $ts.Milliseconds
}

# Normalize any list-ish input (CSV, whitespace, multiline) into a string[]
function Normalize-NameList {
    param([Parameter(ValueFromPipeline = $true)] $Items)
    if ($null -eq $Items) { return @() }
    $acc = New-Object System.Collections.Generic.List[string]
    foreach ($it in @($Items)) {
        if ($null -eq $it) { continue }
        $s = $it.ToString()
        $parts = $s -split '[,\s]+' | Where-Object { $_ -and $_.Trim().Length -gt 0 }
        foreach ($p in $parts) { $acc.Add($p.Trim()) }
    }
    $acc | Select-Object -Unique
}

# Strip leading "+", bullets, dashes, whitespace, and common bidi/control chars
function Clean-Names {
    param([AllowNull()][string[]] $Names)
    if (-not $Names) { return @() }
    @(
        foreach ($n in $Names) {
            if (-not $n) { continue }
            ($n -replace '^[\+\-\u2022\u200E\u200F\u202A-\u202E\s]+', '').Trim()
        }
    ) | Where-Object { $_ } | Select-Object -Unique
}

# Helper to read current PowerState without using --query (avoids az.cmd [] parsing issues on Windows)
function Get-PowerState {
    param(
        [Parameter(Mandatory)][string]$ResourceGroup,
        [Parameter(Mandatory)][string]$VmName
    )
    try {
        $iv = az vm get-instance-view -g $ResourceGroup -n $VmName -o json | ConvertFrom-Json
        $code = ($iv.instanceView.statuses | Where-Object { $_.code -like 'PowerState/*' } |
                 Select-Object -ExpandProperty code -First 1)
        if ([string]::IsNullOrWhiteSpace($code)) { return 'PowerState/unknown' }
        return $code
    }
    catch { return 'PowerState/unknown' }
}

# Ensure you're logged in
try { Invoke-AzCli -Args @('account','show','-o','none') -OkMessage $null }
catch {
    Write-Host "Logging in to Azure..." -ForegroundColor Yellow
    Invoke-AzCli -Args @('login','-o','none') -OkMessage $null
}

# --- Start total stopwatch ----------------------------------------------------
$__totalSW = [System.Diagnostics.Stopwatch]::StartNew()

# --- Resolve & sanitize target VM names --------------------------------------
[string[]]$vmNames = @()

Write-Host "Finding VMs with prefix '$NAME_PREFIX' in RG '$RG'..."
$vmObjs  = az vm list -g $RG -o json | ConvertFrom-Json
$raw     = if ($vmObjs) { ($vmObjs | Where-Object { $_.name -like "$NAME_PREFIX*" } | ForEach-Object { $_.name }) } else { @() }
$vmNames = Clean-Names (Normalize-NameList $raw)

if (-not $vmNames -or $vmNames.Count -eq 0) {
    Write-Host "There are no attendee VMs." -ForegroundColor Yellow
    $__totalSW.Stop()
    Write-Host ("[TIME] Total elapsed: {0}" -f (Format-Elapsed $__totalSW.Elapsed)) -ForegroundColor Green
    return
}
Write-Host ("Found:`n - {0}" -f ($vmNames -join "`n - "))

Write-Host ("Will start the following VMs:`n - {0}" -f ($vmNames -join "`n - "))

foreach ($vm in $vmNames) {
    $vmSW = [System.Diagnostics.Stopwatch]::StartNew()

    $state = Get-PowerState -ResourceGroup $RG -VmName $vm
    Write-Host "`n[$vm] Current state: $state"

    if ($state -eq 'PowerState/running') {
        $vmSW.Stop()
        Write-Host ("[SKIP] {0} is already running. (elapsed {1})" -f $vm, (Format-Elapsed $vmSW.Elapsed)) -ForegroundColor Yellow
        continue
    }

    Invoke-AzCli -Args @('vm','start','-g',$RG,'-n',$vm,'-o','none') -OkMessage "Started $vm"

    $vmSW.Stop()
    Write-Host ("[TIME] VM {0} started in {1}" -f $vm, (Format-Elapsed $vmSW.Elapsed)) -ForegroundColor Cyan
}

$__totalSW.Stop()
Write-Host ("`n[TIME] Total elapsed: {0}" -f (Format-Elapsed $__totalSW.Elapsed)) -ForegroundColor Green
