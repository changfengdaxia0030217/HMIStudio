#!/usr/bin/env bash
set -euo pipefail

echo "==> Installing HMIStudio frontend dependencies"
cd src/HMI.Web
npm install --no-audit --no-fund
npm run build
cd ../..

echo "==> Restoring .NET solution"
dotnet restore HMIStudio.sln

echo "HMIStudio cloud workspace is ready."
