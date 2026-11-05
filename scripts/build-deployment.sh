#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

if ! command -v dotnet >/dev/null 2>&1; then
  echo "Error: .NET SDK is not installed or is not on PATH." >&2
  exit 1
fi

if ! command -v zip >/dev/null 2>&1; then
  echo "Error: the 'zip' command is required to create the deployment package." >&2
  exit 1
fi

STAMP="$(date +%Y%m%d-%H%M%S)"
ZIP="lagrandeur-spa-${STAMP}.zip"

echo "Cleaning previous build output..."
rm -rf bin obj publish

echo "Restoring..."
dotnet restore LaGrandeurSpa.Web.csproj

echo "Building..."
dotnet build LaGrandeurSpa.Web.csproj -c Release --no-restore

echo "Publishing..."
dotnet publish LaGrandeurSpa.Web.csproj -c Release -o publish --no-restore

echo "Creating deployment package..."
(
  cd publish
  zip -qr "../$ZIP" .
)

echo
echo "Deployment package created:"
echo "$ROOT/$ZIP"
