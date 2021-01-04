#!/usr/bin/env pwsh

$script_root = split-path -parent $MyInvocation.MyCommand.Definition

$ARTIFACTS_DIR = Join-Path $script_root "artifacts"

dotnet test
dotnet pack  --output $ARTIFACTS_DIR
