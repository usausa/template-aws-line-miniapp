# Reflect cdk outputs into appsettings.Development.json for local development.
# Usage: ./scripts/update-appsettings.ps1 -Env dev
#
# Uses the local LIFF id (LiffIdLocal output), whose LINE app has the local dev server registered as
# its endpoint URL.

param(
    [ValidateSet('dev', 'prod')]
    [string] $Env = 'dev'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$root = Split-Path -Parent $PSScriptRoot
$outputs = Get-StackOutputs -Root $root -EnvName $Env

$liffId = if ($outputs.LiffIdLocal) { $outputs.LiffIdLocal } else { $outputs.LiffId }

Write-AppSettings -Outputs $outputs -LiffId $liffId `
    -Path (Join-Path $root 'Template.Frontend/wwwroot/appsettings.Development.json')

Write-Host "Run 'dotnet run --project Template.Frontend' (https://localhost:5250) to connect to the $Env stack."
