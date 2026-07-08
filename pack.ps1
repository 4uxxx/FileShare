<#
  Builds, packages (Velopack) and publishes a new FileShare release to GitHub Releases.
  Usage: .\pack.ps1 -Version 1.0.1
  Requires: dotnet, the 'vpk' global tool (dotnet tool install -g vpk), and `gh auth login`.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

if (Test-Path "$root\publish") { Remove-Item "$root\publish" -Recurse -Force }

dotnet publish "$root\src\FileShare\FileShare.csproj" -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false -o "$root\publish"

vpk pack -u FileShare -v $Version -p "$root\publish" -e FileShare.exe `
    --packTitle "FileShare" --packAuthors "4uxxx" -o "$root\Releases"

$token = (& "C:\Program Files\GitHub CLI\gh.exe" auth token)
vpk upload github --repoUrl https://github.com/4uxxx/FileShare -o "$root\Releases" `
    --token $token --publish --releaseName "FileShare $Version"
