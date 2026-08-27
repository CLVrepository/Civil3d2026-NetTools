# ============================================================
# CLV CivilTools - Knowledge Base Upload Cleanup
# ============================================================

$ErrorActionPreference = "Stop"

$RepoRoot = (git rev-parse --show-toplevel).Trim()
$UploadFolder = Join-Path $RepoRoot "KnowledgeBase\UPLOAD"
$UploadRelative = "KnowledgeBase/UPLOAD"
$Manifest = Join-Path $UploadFolder "UPLOAD_MANIFEST.txt"

Set-Location $RepoRoot

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host " CLV Knowledge Base Upload Cleanup" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# ------------------------------------------------------------
# Verify branch
# ------------------------------------------------------------

$Branch = (git branch --show-current).Trim()

if ($Branch -ne "work") {
    Write-Host "ERROR: Current branch is '$Branch'." -ForegroundColor Red
    Write-Host "This workflow is intended for the work branch." -ForegroundColor Yellow
    exit 1
}

# ------------------------------------------------------------
# Verify repository state
#
# Cleanup may operate on the UPLOAD folder only.
# Everything else must be clean.
# ------------------------------------------------------------

$StatusLines = @(git status --short --untracked-files=all)

$OutsideUpload = @(
    $StatusLines | Where-Object {
        if ([string]::IsNullOrWhiteSpace($_) -or $_.Length -lt 4) {
            return $false
        }

        $Path = $_.Substring(3).Trim('"').Replace('\', '/')
        $Path -notlike "$UploadRelative/*"
    }
)

if ($OutsideUpload.Count -gt 0) {
    Write-Host "ERROR: There are changes outside the UPLOAD folder." -ForegroundColor Red
    Write-Host ""
    foreach ($Line in $OutsideUpload) {
        Write-Host "  $Line" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "Resolve or commit those changes before running cleanup." -ForegroundColor Yellow
    exit 1
}

# ------------------------------------------------------------
# Verify upload folder and manifest
# ------------------------------------------------------------

if (-not (Test-Path -LiteralPath $UploadFolder -PathType Container)) {
    Write-Host "ERROR: Upload folder does not exist:" -ForegroundColor Red
    Write-Host "  $UploadFolder"
    exit 1
}

if (-not (Test-Path -LiteralPath $Manifest -PathType Leaf)) {
    Write-Host "ERROR: Upload manifest was not found:" -ForegroundColor Red
    Write-Host "  $Manifest"
    Write-Host ""
    Write-Host "Nothing will be deleted." -ForegroundColor Yellow
    exit 1
}

# ------------------------------------------------------------
# Read and validate manifest
# ------------------------------------------------------------

$Files = @(
    Get-Content -LiteralPath $Manifest |
        Where-Object {
            $_ -and
            -not $_.StartsWith("#") -and
            $_ -notmatch "^\s*$"
        }
)

if ($Files.Count -eq 0) {
    Write-Host "Manifest contains no upload files." -ForegroundColor Yellow
    exit 0
}

$ValidatedFiles = @()

foreach ($RelativePath in $Files) {

    $Normalized = $RelativePath.Trim().Replace('\', '/')

    # Manifest entries must be repository-relative and remain inside UPLOAD.
    if ($Normalized.StartsWith("/") -or
        $Normalized -match "^[A-Za-z]:/" -or
        $Normalized -match "(^|/)\.\.(/|$)" -or
        $Normalized -notlike "$UploadRelative/*") {

        Write-Host "ERROR: Invalid manifest path:" -ForegroundColor Red
        Write-Host "  $RelativePath"
        Write-Host ""
        Write-Host "Manifest entries must remain inside KnowledgeBase/UPLOAD/." -ForegroundColor Yellow
        exit 1
    }

    if ($Normalized -eq "$UploadRelative/README.md" -or
        $Normalized -eq "$UploadRelative/UPLOAD_MANIFEST.txt") {

        Write-Host "ERROR: Protected file listed in manifest:" -ForegroundColor Red
        Write-Host "  $RelativePath"
        Write-Host ""
        exit 1
    }

    $ValidatedFiles += $Normalized
}

Write-Host ""
Write-Host "Files scheduled for removal:" -ForegroundColor Yellow
Write-Host ""

foreach ($File in $ValidatedFiles) {
    Write-Host "  $File"
}

Write-Host ""

$Confirm = Read-Host "Remove these temporary upload files? (Y/N)"

if ($Confirm -notmatch "^[Yy]$") {
    Write-Host "Cleanup cancelled." -ForegroundColor Yellow
    exit 0
}

# ------------------------------------------------------------
# Remove manifest-listed files
# ------------------------------------------------------------

foreach ($RelativePath in $ValidatedFiles) {

    $FullPath = Join-Path $RepoRoot ($RelativePath.Replace('/', '\'))

    if (Test-Path -LiteralPath $FullPath -PathType Leaf) {
        Remove-Item -LiteralPath $FullPath -Force
        Write-Host "Removed: $RelativePath" -ForegroundColor DarkYellow
    }
    else {
        Write-Host "File already missing: $RelativePath" -ForegroundColor DarkYellow
    }
}

# Remove manifest itself.
if (Test-Path -LiteralPath $Manifest -PathType Leaf) {
    Remove-Item -LiteralPath $Manifest -Force
}

# Stage only the UPLOAD cleanup.
git add -A -- "KnowledgeBase/UPLOAD"

# ------------------------------------------------------------
# Final status
# ------------------------------------------------------------

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host " Cleanup staged" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""

git diff --cached --name-status

Write-Host ""
Write-Host "README.md remains in the UPLOAD folder." -ForegroundColor Green
Write-Host "No commit or push was performed." -ForegroundColor Yellow
Write-Host "Review the staged changes in Visual Studio before committing." -ForegroundColor Yellow
Write-Host ""
