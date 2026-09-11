# Generate the app's ES256 JWT signing key and store it in Secrets Manager.
# Usage: ./scripts/init-jwt-key.ps1 -Env dev [-Force]
#
# The CDK stack creates the secret with a placeholder value (no private key material is ever in the
# template or CloudFormation events). This puts a real key in. It is idempotent: if a real key is
# already present it does nothing, so re-running the setup is safe. -Force regenerates the key,
# which invalidates every app JWT already issued (all users must re-exchange on their next call).

param(
    [ValidateSet('dev', 'prod')]
    [string] $Env = 'dev',
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$root = Split-Path -Parent $PSScriptRoot
$outputs = Get-StackOutputs -Root $root -EnvName $Env
$arn = $outputs.JwtSecretArn

# Skip if a real key is already stored (unless -Force).
$current = aws secretsmanager get-secret-value --secret-id $arn --query 'SecretString' --output text 2>$null
if (-not $Force -and $current -and ($current -notlike 'PLACEHOLDER*')) {
    Write-Host "A signing key is already set for $Env. Use -Force to replace it (invalidates existing tokens)."
    return
}

# Generate a P-256 (ES256) private key and export it as PKCS#8 PEM.
$ecdsa = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve+NamedCurves]::nistP256)
try {
    $der = $ecdsa.ExportPkcs8PrivateKey()
}
finally {
    $ecdsa.Dispose()
}

$b64 = [Convert]::ToBase64String($der)
$wrapped = ($b64 -replace '(.{64})', "`$1`n").TrimEnd()
$pem = "-----BEGIN PRIVATE KEY-----`n$wrapped`n-----END PRIVATE KEY-----`n"

# Pass the PEM via a temp file so newlines survive (file:// takes the content verbatim).
$temp = Join-Path ([System.IO.Path]::GetTempPath()) "jwt-key-$([System.Guid]::NewGuid().ToString('N')).pem"
Set-Content -Path $temp -Value $pem -NoNewline -Encoding ascii
try {
    aws secretsmanager put-secret-value --secret-id $arn --secret-string "file://$temp" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'put-secret-value failed.' }
}
finally {
    Remove-Item $temp -Force -ErrorAction SilentlyContinue
}

Write-Host "JWT signing key set for $Env."
