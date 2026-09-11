# Shared helpers dot-sourced by the other scripts.

$Script:Region = 'ap-northeast-1'
$Script:StackPrefix = 'template-aws-line-miniapp'

function Get-StackOutputs {
    param(
        [Parameter(Mandatory = $true)] [string] $Root,
        [Parameter(Mandatory = $true)] [string] $EnvName
    )

    $path = Join-Path $Root "cdk-outputs.$EnvName.json"
    if (-not (Test-Path $path)) {
        throw "cdk outputs not found: $path`nRun this in the Template.IaC directory: npx --yes aws-cdk@latest deploy -c env=$EnvName --outputs-file ../cdk-outputs.$EnvName.json"
    }

    $stack = "$Script:StackPrefix-$EnvName"
    $outputs = (Get-Content $path -Raw | ConvertFrom-Json).$stack
    if ($null -eq $outputs) {
        throw "Stack $stack not found in outputs: $path"
    }

    return $outputs
}

function Write-AppSettings {
    param(
        [Parameter(Mandatory = $true)] $Outputs,
        [Parameter(Mandatory = $true)] [string] $Path,
        [Parameter(Mandatory = $true)] [string] $LiffId
    )

    # All values here are public by design; the security boundary is the app JWT (see SPEC 7.5).
    $settings = [ordered]@{
        App = [ordered]@{
            LiffId      = $LiffId
            ApiEndpoint = $Outputs.ApiEndpoint
        }
    }

    $settings | ConvertTo-Json -Depth 5 | Set-Content -Path $Path -Encoding utf8
    Write-Host "Updated: $Path"
}
