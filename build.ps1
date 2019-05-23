$PSScriptRoot = split-path -parent $MyInvocation.MyCommand.Definition

$ARTIFACTS_DIR = Join-Path $PSScriptRoot "artifacts"

dotnet test
dotnet pack  --output $ARTIFACTS_DIR
