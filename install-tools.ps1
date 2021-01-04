#!/usr/bin/env pwsh

$script_root = split-path -parent $MyInvocation.MyCommand.Definition

$ARTIFACTS_DIR = Join-Path $script_root "artifacts"
$TOOLS_DIR = Join-Path $script_root "tools"

function Resolve-Version() {
    $GitVersionDir = (Get-ChildItem $TOOLS_DIR -Recurse | Where-Object { $_.PSIsContainer -and $_.Name.StartsWith("GitVersion.CommandLine") }).Name
    $output = & "$TOOLS_DIR\$GitVersionDir\tools\GitVersion.exe" /output json
    if ($LASTEXITCODE -ne 0) {
        if ($output -is [array]) {
            Write-Error ($output -join "`r`n")
        }
        else {
            Write-Error $output
        }
        throw "GitVersion Exit Code: $LASTEXITCODE"
    }
    $versionInfoJson = $output -join "`n"
    $versionInfo = $versionInfoJson | ConvertFrom-Json

    return $versionInfo.NuGetVersion
}

#$VERSION = Resolve-Version
$VERSION = "1.0.0"

# Uninstall the tools
dotnet tool uninstall dotnet-deps --tool-path $TOOLS_DIR

# Install the tools
dotnet tool install dotnet-deps --add-source $ARTIFACTS_DIR --version $VERSION --tool-path $TOOLS_DIR

# Polyfill
if (-not (Get-Command Prepend-Path -ErrorAction SilentlyContinue | Test-Path)) {
    function Prepend-Path() {
        [CmdletBinding()]
        param (
            [Parameter(
                Mandatory = $True,
                ValueFromPipeline = $True,
                ValueFromPipelineByPropertyName = $True,
                HelpMessage = 'What directory would you like to prepend to the PATH?')]
            [Alias('dir')]
            [string]$Directory
        )

        PROCESS {
            $OldPath = Get-Content Env:\Path
            if ($env:PATH | Select-String -SimpleMatch $Directory) {
                Write-Verbose "'$Directory' is already present in PATH"
            }
            else {
                if (-not (Test-Path $Directory)) {
                    Write-Verbose "'$Directory' does not exist in the filesystem"
                }
                else {
                    $NewPath = $Directory + ";" + $OldPath
                    Set-Content Env:\Path $NewPath
                }
            }
        }
    }
}

Prepend-Path $TOOLS_DIR

# Test installation
deps --help
