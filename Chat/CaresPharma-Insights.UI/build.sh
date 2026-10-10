#!/bin/bash

# Download the official .NET install script
wget https://dot.net/v1/dotnet-install.sh -O dotnet-install.sh
chmod +x ./dotnet-install.sh

# Install .NET 10.0 SDK
./dotnet-install.sh --channel 10.0

# Add .NET to PATH so the next commands can use it
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$HOME/.dotnet:$PATH

# Verify installation
dotnet --version

# Publish the Blazor WebAssembly app
dotnet publish -c Release -o release /p:EnvironmentName=DevServer

# Fix for Netlify: .NET 10 adds a hash to blazor.webassembly.js but index.html expects the unhashed name.
# Copy the hashed file so it's available under the unhashed name as well.
cp release/wwwroot/_framework/blazor.webassembly.*.js release/wwwroot/_framework/blazor.webassembly.js 2>/dev/null || true
