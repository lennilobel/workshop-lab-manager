# 01-Publish-MasterImage.ps1

# Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass

# Fail on any error
$ErrorActionPreference = 'Stop'

# --- config (edit as needed) -------------------------------------------------
$RESOURCE_GROUP = "sql-hol-rg"
$SOURCE_VM      = "vm-sql-hol-master04"
$SNAPSHOT_NAME  = "${SOURCE_VM}_OsDisk_1_snapshot"

# gallery assets
$GALLERY        = "vm_sql_hol_gallery"
$IMAGE_NAME     = "vm-sql-hol-master04-image"
$IMAGE_VERSION  = "1.0.0"

# Publisher/Offer/SKU (required for image definition)
$PUBLISHER     = "SqlHol04"
$OFFER         = "Windows-TrustedLaunch"
$SKU           = "gen2-specialized"
# ---------------------------------------------------------------------------

function Invoke-AzCli {
    param([Parameter(Mandatory)] [string[]] $Args, [string] $OkMessage = "OK")
    & az @Args
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI failed: az $($Args -join ' ')"
    }
    if ($OkMessage) { Write-Host "[OK] $OkMessage" }
}

# Ensure you're logged in
try { Invoke-AzCli -Args @('account','show','-o','none') -OkMessage $null }
catch {
    Write-Host "Logging in to Azure..." -ForegroundColor Yellow
    Invoke-AzCli -Args @('login','-o','none') -OkMessage $null
}

# 1) Resolve location from source VM
Write-Host "[Step 1/9] Resolving source VM location..."
$LOCATION = (& az vm show -g $RESOURCE_GROUP -n $SOURCE_VM --query location -o tsv).Trim()
if (-not $LOCATION) { throw "Could not resolve location for $SOURCE_VM." }
Write-Host "[OK] Location: $LOCATION"

# 2) Deallocate source VM (safe for snapshot)
Write-Host "`n[Step 2/9] Deallocating source VM '$SOURCE_VM'..."
Invoke-AzCli -Args @('vm','deallocate','-g',$RESOURCE_GROUP,'-n',$SOURCE_VM,'-o','none') -OkMessage "VM deallocated"

# 3) Get OS disk id
Write-Host "`n[Step 3/9] Getting OS disk ID..."
$OSDISK_ID = (& az vm show -g $RESOURCE_GROUP -n $SOURCE_VM --query "storageProfile.osDisk.managedDisk.id" -o tsv).Trim()
if (-not $OSDISK_ID) { throw "Could not resolve OS disk id." }
Write-Host "[OK] OS disk ID: $OSDISK_ID"

# 4) Snapshot the specialized OS disk
Write-Host "`n[Step 4/9] Creating snapshot of the specialized OS disk..."
Invoke-AzCli -Args @('snapshot','create',
    '-g',           $RESOURCE_GROUP,
    '-n',           $SNAPSHOT_NAME,
    '--source',     $OSDISK_ID,
    '--location',   $LOCATION,
    '-o',           'none'
) -OkMessage "Snapshot created: $SNAPSHOT_NAME"

# 5) Ensure gallery & image definition exist
Write-Host "`n[Step 5/9] Ensuring gallery '$GALLERY' and image definition '$IMAGE_NAME' exist..."
$galleryExists = $false
try {
    & az sig show -g $RESOURCE_GROUP --gallery-name $GALLERY -o none 2>$null
    if ($LASTEXITCODE -eq 0) { $galleryExists = $true }
} catch { }

if (-not $galleryExists) {
    Invoke-AzCli -Args @('sig','create','-g',$RESOURCE_GROUP,'--gallery-name',$GALLERY,'--location',$LOCATION,'-o','none') -OkMessage "Gallery created"
} else {
    Write-Host "[OK] Gallery exists"
}

$imgDefExists = (& az sig image-definition list -g $RESOURCE_GROUP --gallery-name $GALLERY `
                  --query "[?name=='$IMAGE_NAME'] | length(@)" -o tsv).Trim()
if (-not $imgDefExists -or $imgDefExists -eq '0') {
    Invoke-AzCli -Args @('sig','image-definition','create',
        '-g',                   $RESOURCE_GROUP,
        '--gallery-name',       $GALLERY,
        '-i',                   $IMAGE_NAME,
        '--publisher',          $PUBLISHER,
        '--offer',              $OFFER,
        '--sku',                $SKU,
        '--os-type',            'Windows',
        '--os-state',           'Specialized',
        '--hyper-v-generation', 'V2',
        '--features',           'SecurityType=TrustedLaunch',
        '--location',           $LOCATION,
        '-o',                   'none'
    ) -OkMessage "Image definition created"
} else {
    Write-Host "[OK] Image definition exists"
}

# 6) Delete existing image version if it already exists
Write-Host "`n[Step 6/9] Checking for existing image version '$IMAGE_VERSION'..."
$versionExists = $false
try {
    & az sig image-version show `
        -g $RESOURCE_GROUP `
        --gallery-name $GALLERY `
        --gallery-image-definition $IMAGE_NAME `
        --gallery-image-version $IMAGE_VERSION `
        -o none 2>$null
    if ($LASTEXITCODE -eq 0) {
        $versionExists = $true
    }
}
catch { }
if ($versionExists) {
    Write-Host "[INFO] Existing image version found. Deleting..." -ForegroundColor Yellow
    Invoke-AzCli -Args @(
        'sig','image-version','delete',
        '-g', $RESOURCE_GROUP,
        '--gallery-name', $GALLERY,
        '--gallery-image-definition', $IMAGE_NAME,
        '--gallery-image-version', $IMAGE_VERSION,
        '-o', 'none'
    ) -OkMessage "Existing image version deleted"

    # Wait for deletion completion
    Write-Host "[INFO] Waiting for image version deletion to complete..."
    Start-Sleep -Seconds 15
}

# 7) Publish image version from snapshot
Write-Host "`n[Step 7/9] Publishing image version '$IMAGE_VERSION'..."
Invoke-AzCli -Args @('sig','image-version','create',
    '-g',                           $RESOURCE_GROUP,
    '--gallery-name',               $GALLERY,
    '--gallery-image-definition',   $IMAGE_NAME,
    '--gallery-image-version',      $IMAGE_VERSION,
    '--os-snapshot',                $SNAPSHOT_NAME,
    '--target-regions',             $LOCATION,
    '-o',                           'none'
) -OkMessage "Image version published"

# Output the full image version resource ID for reuse
$SUBSCRIPTION_ID = (& az account show --query id -o tsv).Trim()
$GALLERY_IMAGE_ID = "/subscriptions/$SUBSCRIPTION_ID/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Compute/galleries/$GALLERY/images/$IMAGE_NAME/versions/$IMAGE_VERSION"
Write-Host "`n[RESULT] Gallery Image Version ID:"
Write-Host $GALLERY_IMAGE_ID -ForegroundColor Cyan

# 8) Delete snapshot (no longer needed after image version creation)
Write-Host "`n[Step 8/9] Deleting snapshot '$SNAPSHOT_NAME'..."
Invoke-AzCli -Args @('snapshot','delete',
    '-g', $RESOURCE_GROUP,
    '-n', $SNAPSHOT_NAME,
    '-o', 'none'
) -OkMessage "Snapshot deleted"

# 9) Replicate image version to additional regions
Write-Host "`n[Step 9/9] Replicating image version to other regions..."
Invoke-AzCli -Args @(
    'sig','image-version','update',
    '-g',                           $RESOURCE_GROUP,
    '--gallery-name',               $GALLERY,
    '--gallery-image-definition',   $IMAGE_NAME,
    '--gallery-image-version',      $IMAGE_VERSION,
    '--target-regions',
        'eastus',
        'eastus2',
        'southcentralus',
        'northcentralus',
        'centralus',
    '-o',                           'none'
) -OkMessage "Image replication started"

Write-Host "[DONE] Image publishing and replication completed successfully." -ForegroundColor Green
pause
