# Validate spec coverage - check for orphaned test fragments
param()

# Extract specs first
& maxon extract-specs | Out-Null

# Load manifest
$manifestPath = "language-tests\.spec-manifest.json"
if (-not (Test-Path $manifestPath)) {
    Write-Host "ERROR: Manifest file not found at $manifestPath" -ForegroundColor Red
    exit 1
}

$manifest = Get-Content $manifestPath | ConvertFrom-Json
$specFragments = @($manifest.fragments.PSObject.Properties.Name)

# Get all fragment files
$allFragments = @(Get-ChildItem "language-tests\fragments\*.test" | ForEach-Object { $_.Name })

# Find orphans
$orphans = @($allFragments | Where-Object { 
    $_ -notin $specFragments -and 
    $_ -notlike "control flow.*" -and 
    $_ -notlike "variables.*"
})

Write-Host "`nSpec Coverage Validation Results:" -ForegroundColor Cyan
Write-Host "=================================" -ForegroundColor Cyan
Write-Host "Total fragments: $($allFragments.Count)"
Write-Host "Defined in specs: $($specFragments.Count)"
Write-Host "Orphaned (not in specs): $($orphans.Count)"

if ($orphans.Count -gt 0) {
    Write-Host "`nWARNING: The following fragments are not defined in any spec file:" -ForegroundColor Yellow
    foreach ($orphan in $orphans | Sort-Object) {
        Write-Host "  - $orphan" -ForegroundColor Yellow
    }
    Write-Host "`nThese fragments should be converted to spec files in specs/" -ForegroundColor Yellow
} else {
    Write-Host "`nAll fragments are defined in spec files!" -ForegroundColor Green
}

exit 0
