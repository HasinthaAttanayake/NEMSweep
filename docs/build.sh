#!/bin/sh
# Cloudflare Pages build for the documentation site.
#
# Pages images do not ship .NET 10, so install it locally, then restore the pinned docfx from
# .config/dotnet-tools.json and build the docset. Output directory is docs/_site.
set -e

curl -sSL https://dot.net/v1/dotnet-install.sh > dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh -c 10.0 -InstallDir ./dotnet
./dotnet/dotnet --version
./dotnet/dotnet tool restore
./dotnet/dotnet docfx docs/docfx.json --warningsAsErrors
