# ============================================================
# CLV CivilTools - Knowledge Base Upload Cleanup
# Branch: kb-upload
# ============================================================

$ErrorActionPreference = "Stop"

$RepoRoot = (git rev-parse --show-toplevel).Trim()
$UploadFolder = Join-Path $RepoRoot "KnowledgeBase\UPLOAD"
$Manifest = Join-Path $UploadFolder "UPLOAD_MANIFEST.txt"

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host " CLV Knowledge Base Upload Cleanup" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

Set-Location $RepoRoot

# ------------------------------------------------------------
# Verify branch
# ------------------------------------------------------------

$Branch = (git branch --show-current).Trim()

if ($Branch -ne "kb-upload") {
    Write-Host "ERROR: Current branch is '$Branch'." -ForegroundColor Red
    Write-Host "Switch to kb-upload before running cleanup."
    exit 1
}

# ------------------------------------------------------------
# Verify clean working tree
# ------------------------------------------------------------

$Status = git status --short

if ($Status) {
    Write-Host "ERROR: Working tree is not clean." -ForegroundColor Red
    Write-Host ""
    git status --short
    Write-Host ""
    Write-Host "Resolve existing changes before cleanup." -ForegroundColor Yellow
    exit 1
}

# ------------------------------------------------------------
# Pull latest upload branch
# ------------------------------------------------------------

Write-Host "Checking GitHub for updates..." -ForegroundColor Cyan

git pull --ff-only origin kb-upload

# ------------------------------------------------------------
# Verify manifest
# ------------------------------------------------------------

if (-not (Test-Path $Manifest)) {
    Write-Host "ERROR: Upload manifest was not found:" -ForegroundColor Red
    Write-Host $Manifest
    Write-Host ""
    Write-Host "Nothing will be deleted." -ForegroundColor Yellow
    exit 1
}

# ------------------------------------------------------------
# Read manifest
# ------------------------------------------------------------

$Files = Get-Content $Manifest |
    Where-Object {
        $_ -and
        -not $_.StartsWith("#") -and
        $_ -notmatch "^\s*$"
    }

if ($Files.Count -eq 0) {
    Write-Host "Manifest contains no upload files." -ForegroundColor Yellow
    exit 0
}

Write-Host ""
Write-Host "Files scheduled for removal:" -ForegroundColor Yellow
Write-Host ""

foreach ($File in $Files) {
    Write-Host "  $File"
}

Write-Host ""

$Confirm = Read-Host "Remove these files from the upload branch? (Y/N)"

if ($Confirm -notmatch "^[Yy]$") {
    Write-Host "Cleanup cancelled." -ForegroundColor Yellow
    exit 0
}

# ------------------------------------------------------------
# Remove files listed in manifest
# ------------------------------------------------------------

foreach ($RelativePath in $Files) {

    $FullPath = Join-Path $RepoRoot $RelativePath

    if (Test-Path $FullPath) {
        git rm -- "$RelativePath"
    }
    else {
        Write-Host "File already missing: $RelativePath" -ForegroundColor DarkYellow
    }
}

# Remove manifest itself
if (Test-Path $Manifest) {
    git rm -- "KnowledgeBase/UPLOAD/UPLOAD_MANIFEST.txt"
}

# ------------------------------------------------------------
# Commit cleanup
# ------------------------------------------------------------

Write-Host ""
Write-Host "Committing cleanup..." -ForegroundColor Cyan

git commit -m "Clean Knowledge Base upload staging area"

# ------------------------------------------------------------
# Push cleanup
# ------------------------------------------------------------

Write-Host ""
Write-Host "Pushing cleanup to origin/kb-upload..." -ForegroundColor Cyan

git push origin kb-upload

# ------------------------------------------------------------
# Final status
# ------------------------------------------------------------

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host " Cleanup complete" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""

git status --short

Write-Host ""
Write-Host "README.md remains in the UPLOAD folder."
Write-Host "Temporary upload files have been removed."
Write-Host ""