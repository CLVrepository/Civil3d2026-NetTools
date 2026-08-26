# ============================================================
# CLV CivilTools - Knowledge Base Upload
# Branch: kb-upload
# ============================================================

$ErrorActionPreference = "Stop"

$RepoRoot = (git rev-parse --show-toplevel).Trim()
$UploadFolder = Join-Path $RepoRoot "KnowledgeBase\UPLOAD"
$UploadRelative = "KnowledgeBase/UPLOAD"
$Manifest = Join-Path $UploadFolder "UPLOAD_MANIFEST.txt"

Write-Host ""
Write-Host "============================================" -ForegroundColor Cyan
Write-Host " CLV Knowledge Base Upload" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

Set-Location $RepoRoot

# ------------------------------------------------------------
# Verify branch
# ------------------------------------------------------------

$Branch = (git branch --show-current).Trim()

if ($Branch -ne "kb-upload") {
    Write-Host "ERROR: Current branch is '$Branch'." -ForegroundColor Red
    Write-Host "Switch to kb-upload before running this script."
    exit 1
}

# ------------------------------------------------------------
# Verify repository state
#
# Changes inside KnowledgeBase/UPLOAD are expected.
# Everything else must be clean.
# ------------------------------------------------------------

$StatusLines = @(git status --short --untracked-files=all)

$OutsideUpload = @(
    $StatusLines | Where-Object {
        if (-not $_ -or $_.Length -lt 4) {
            return $false
        }

        $Path = $_.Substring(3).Trim('"').Replace('\', '/')

        $Path -notlike "$UploadRelative/*"
    }
)

if ($OutsideUpload.Count -gt 0) {
    Write-Host "ERROR: There are changes outside the UPLOAD folder." -ForegroundColor Red
    Write-Host ""
    Write-Host "Those changes must be resolved before uploading." -ForegroundColor Yellow
    Write-Host ""

    foreach ($Line in $OutsideUpload) {
        Write-Host "  $Line" -ForegroundColor Red
    }

    Write-Host ""
    Write-Host "Expected upload staging area:" -ForegroundColor Yellow
    Write-Host "  KnowledgeBase\UPLOAD\" -ForegroundColor Yellow
    Write-Host ""

    exit 1
}

# ------------------------------------------------------------
# Verify upload folder
# ------------------------------------------------------------

if (-not (Test-Path $UploadFolder)) {
    Write-Host "ERROR: Upload folder does not exist:" -ForegroundColor Red
    Write-Host $UploadFolder
    exit 1
}

# ------------------------------------------------------------
# Find upload files
#
# README.md is permanent and is excluded.
# Existing manifest is also excluded.
# ------------------------------------------------------------

$Files = @(
    Get-ChildItem $UploadFolder -File -Recurse |
        Where-Object {
            $_.Name -ne "README.md" -and
            $_.Name -ne "UPLOAD_MANIFEST.txt"
        }
)

if ($Files.Count -eq 0) {
    Write-Host "No files found to upload." -ForegroundColor Yellow
    exit 0
}

Write-Host "Files found for upload:" -ForegroundColor Green
Write-Host ""

foreach ($File in $Files) {
    $Relative = $File.FullName.Substring($RepoRoot.Length + 1)
    Write-Host "  $Relative"
}

Write-Host ""

$Confirm = Read-Host "Upload these files to GitHub? (Y/N)"

if ($Confirm -notmatch "^[Yy]$") {
    Write-Host "Upload cancelled." -ForegroundColor Yellow
    exit 0
}

# ------------------------------------------------------------
# Create manifest
# ------------------------------------------------------------

$ManifestLines = @(
    "# CLV Knowledge Base Upload Manifest"
    "# Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    "# Files below are temporary upload items."
    "# README.md is permanent and is not listed."
    ""
)

foreach ($File in $Files) {
    $Relative = $File.FullName.Substring($RepoRoot.Length + 1)
    $ManifestLines += $Relative
}

$ManifestLines | Set-Content $Manifest -Encoding UTF8

# ------------------------------------------------------------
# Stage upload folder
# ------------------------------------------------------------

Write-Host "Staging upload..." -ForegroundColor Cyan

git add -- "KnowledgeBase/UPLOAD"

# ------------------------------------------------------------
# Show staged files
# ------------------------------------------------------------

Write-Host ""
Write-Host "Files staged:" -ForegroundColor Green
git diff --cached --name-status
Write-Host ""

# ------------------------------------------------------------
# Commit
# ------------------------------------------------------------

$CommitMessage = "Upload Knowledge Base staging files"

git commit -m $CommitMessage

# ------------------------------------------------------------
# Push
# ------------------------------------------------------------

Write-Host ""
Write-Host "Pushing to origin/kb-upload..." -ForegroundColor Cyan

git push origin kb-upload

# ------------------------------------------------------------
# Final status
# ------------------------------------------------------------

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host " Upload complete" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""

git status --short

Write-Host ""
Write-Host "The uploaded files are now available on GitHub."
Write-Host "The manifest records exactly what was uploaded."
Write-Host ""