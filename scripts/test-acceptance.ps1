# Automated security acceptance tests (SPEC 14), runnable without a real LINE handshake.
# Usage: ./scripts/test-acceptance.ps1 -Env dev
#
# Requires the dev stack deployed with testJwks=true. The script generates a P-256 key, publishes
# its public half as the test JWKS the backend validates against, and forges LINE-shaped ID tokens
# with the private half. This exercises the entire auth path (POST /api/auth/line onward) on real
# AWS infrastructure. The test key is NOT a real LINE key and only works because dev points
# LINE_JWKS_URL at this JWKS.

param(
    [ValidateSet('dev')]
    [string] $Env = 'dev'
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')

$root = Split-Path -Parent $PSScriptRoot
$outputs = Get-StackOutputs -Root $root -EnvName $Env

$apiEndpoint = $outputs.ApiEndpoint          # https://<domain>/api  (through CloudFront)
$directBase = $outputs.DirectApiEndpoint     # https://<id>.execute-api...  (bypasses CloudFront)
$channelId = $outputs.LineChannelId
$bucket = $outputs.AppBucketName
$distId = $outputs.DistributionId
$domain = $outputs.CloudFrontDomain

#--------------------------------------------------------------------------------
# Encoding helpers
#--------------------------------------------------------------------------------

function ConvertTo-Base64Url {
    param([byte[]] $Bytes)
    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function ConvertFrom-Base64Url {
    param([string] $Text)
    $t = $Text.Replace('-', '+').Replace('_', '/')
    switch ($t.Length % 4) { 2 { $t += '==' } 3 { $t += '=' } }
    return [Convert]::FromBase64String($t)
}

#--------------------------------------------------------------------------------
# Test signing key (persisted so repeated runs keep the same JWKS and stay within the
# backend's 15-minute key cache)
#--------------------------------------------------------------------------------

$keyFile = Join-Path $root 'jwt-test-key.pem'
$testKey = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve+NamedCurves]::nistP256)
if (Test-Path $keyFile) {
    $testKey.ImportFromPem((Get-Content $keyFile -Raw))
}
else {
    $der = $testKey.ExportPkcs8PrivateKey()
    $b64 = [Convert]::ToBase64String($der)
    $wrapped = ($b64 -replace '(.{64})', "`$1`n").TrimEnd()
    Set-Content -Path $keyFile -Value "-----BEGIN PRIVATE KEY-----`n$wrapped`n-----END PRIVATE KEY-----`n" -NoNewline -Encoding ascii
}

$pub = $testKey.ExportParameters($false)
$jwk = [ordered]@{
    kty = 'EC'; crv = 'P-256'; alg = 'ES256'; use = 'sig'; kid = 'test-key'
    x   = ConvertTo-Base64Url $pub.Q.X
    y   = ConvertTo-Base64Url $pub.Q.Y
}
$jwksJson = @{ keys = @($jwk) } | ConvertTo-Json -Depth 5 -Compress

#--------------------------------------------------------------------------------
# Token forging
#--------------------------------------------------------------------------------

function New-LineIdToken {
    param(
        [string] $Sub,
        [string] $Aud = $channelId,
        [int] $ExpiresInMinutes = 30
    )
    $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
    $header = @{ alg = 'ES256'; typ = 'JWT'; kid = 'test-key' } | ConvertTo-Json -Compress
    $payload = @{ iss = 'https://access.line.me'; sub = $Sub; aud = $Aud; iat = $now; exp = ($now + $ExpiresInMinutes * 60) } | ConvertTo-Json -Compress

    $h = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($header))
    $p = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes($payload))
    $signingInput = "$h.$p"

    # ECDsa.SignData(data, hashAlgorithm) returns the IEEE P1363 fixed-size r||s signature, which is
    # exactly the ES256 JWS format (unlike the DER form). No DSASignatureFormat overload is needed.
    $sig = $testKey.SignData(
        [Text.Encoding]::ASCII.GetBytes($signingInput),
        [System.Security.Cryptography.HashAlgorithmName]::SHA256)
    return "$signingInput.$(ConvertTo-Base64Url $sig)"
}

function Get-JwtSub {
    param([string] $Token)
    $payload = [Text.Encoding]::UTF8.GetString((ConvertFrom-Base64Url $Token.Split('.')[1]))
    return ($payload | ConvertFrom-Json).sub
}

#--------------------------------------------------------------------------------
# HTTP helper
#--------------------------------------------------------------------------------

function Invoke-Api {
    param(
        [string] $Method,
        [string] $Uri,
        [string] $Token,
        [string] $Body
    )
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }

    $params = @{ Method = $Method; Uri = $Uri; Headers = $headers; SkipHttpErrorCheck = $true }
    if ($Body) { $params['Body'] = $Body; $params['ContentType'] = 'application/json' }

    return Invoke-WebRequest @params
}

function Get-AppToken {
    param([string] $LineUserId)
    $idToken = New-LineIdToken -Sub $LineUserId
    $res = Invoke-Api -Method POST -Uri "$apiEndpoint/auth/line" -Body (@{ idToken = $idToken } | ConvertTo-Json -Compress)
    if ($res.StatusCode -ne 200) { throw "auth/line failed for $LineUserId (status $($res.StatusCode))" }
    return ($res.Content | ConvertFrom-Json).token
}

function New-LineUserId {
    return 'U' + (-join ((1..32) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) }))
}

#--------------------------------------------------------------------------------
# Publish the test JWKS and wait for CloudFront to serve it
#--------------------------------------------------------------------------------

Write-Host 'Publishing test JWKS...'
$jwksFile = Join-Path $root 'test-jwks.json'
Set-Content -Path $jwksFile -Value $jwksJson -NoNewline -Encoding ascii
aws s3 cp $jwksFile "s3://$bucket/test-jwks.json" --content-type 'application/json' --cache-control 'no-cache' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Uploading test-jwks.json failed.' }
aws cloudfront create-invalidation --distribution-id $distId --paths '/test-jwks.json' | Out-Null

$jwksUrl = "https://$domain/test-jwks.json"
$served = $false
for ($i = 0; $i -lt 30; $i++) {
    $r = Invoke-WebRequest -Uri $jwksUrl -SkipHttpErrorCheck
    if (($r.StatusCode -eq 200) -and ($r.Content -match 'test-key')) { $served = $true; break }
    Start-Sleep -Seconds 5
}
if (-not $served) { throw "Test JWKS did not become available at $jwksUrl" }
Write-Host 'Test JWKS is live.'
Write-Host ''

#--------------------------------------------------------------------------------
# Tests
#--------------------------------------------------------------------------------

$results = [System.Collections.Generic.List[object]]::new()
function Test-Case {
    param([string] $Name, [bool] $Pass, [string] $Detail)
    $results.Add([pscustomobject]@{ Name = $Name; Result = if ($Pass) { 'PASS' } else { 'FAIL' }; Detail = $Detail })
}

$userMain = New-LineUserId
$tokenMain = Get-AppToken $userMain

# 1. No token -> 401
$r = Invoke-Api -Method GET -Uri "$apiEndpoint/data"
Test-Case '1. GET /data without token' ($r.StatusCode -eq 401) "status=$($r.StatusCode)"

# 2. ?userId is ignored (the API has no such parameter)
$withParam = Invoke-Api -Method GET -Uri "$apiEndpoint/data?userId=usr_bogus" -Token $tokenMain
$without = Invoke-Api -Method GET -Uri "$apiEndpoint/data" -Token $tokenMain
Test-Case '2. ?userId ignored' (($withParam.StatusCode -eq $without.StatusCode) -and ($withParam.StatusCode -in 200, 204)) "with=$($withParam.StatusCode) without=$($without.StatusCode)"

# 3. Tampered sub -> 401 (signature no longer matches)
$parts = $tokenMain.Split('.')
$payloadObj = [Text.Encoding]::UTF8.GetString((ConvertFrom-Base64Url $parts[1])) | ConvertFrom-Json
$payloadObj.sub = 'usr_attacker'
$tamperedPayload = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes(($payloadObj | ConvertTo-Json -Compress)))
$tampered = "$($parts[0]).$tamperedPayload.$($parts[2])"
$r = Invoke-Api -Method GET -Uri "$apiEndpoint/data" -Token $tampered
Test-Case '3. Tampered sub' ($r.StatusCode -eq 401) "status=$($r.StatusCode)"

# 4. alg=none -> 401
$noneHeader = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes((@{ alg = 'none'; typ = 'JWT' } | ConvertTo-Json -Compress)))
$nonePayload = ConvertTo-Base64Url ([Text.Encoding]::UTF8.GetBytes((@{ iss = 'template-aws-line-miniapp'; aud = 'miniapp'; sub = 'usr_attacker'; exp = ([DateTimeOffset]::UtcNow.ToUnixTimeSeconds() + 3600) } | ConvertTo-Json -Compress)))
$r = Invoke-Api -Method GET -Uri "$apiEndpoint/data" -Token "$noneHeader.$nonePayload."
Test-Case '4. alg=none' ($r.StatusCode -eq 401) "status=$($r.StatusCode)"

# 5. Expired LINE ID token -> /auth/line 401 (exp enforcement)
$expired = New-LineIdToken -Sub (New-LineUserId) -ExpiresInMinutes -10
$r = Invoke-Api -Method POST -Uri "$apiEndpoint/auth/line" -Body (@{ idToken = $expired } | ConvertTo-Json -Compress)
Test-Case '5. Expired LINE token' ($r.StatusCode -eq 401) "status=$($r.StatusCode)"

# 6. Wrong audience LINE token -> /auth/line 401
$wrongAud = New-LineIdToken -Sub (New-LineUserId) -Aud '9999999999'
$r = Invoke-Api -Method POST -Uri "$apiEndpoint/auth/line" -Body (@{ idToken = $wrongAud } | ConvertTo-Json -Compress)
Test-Case '6. Wrong audience' ($r.StatusCode -eq 401) "status=$($r.StatusCode)"

# 7. Optimistic lock: stale version -> 409
$first = Invoke-Api -Method PUT -Uri "$apiEndpoint/data" -Token $tokenMain -Body (@{ data = '{"n":1}'; version = 0 } | ConvertTo-Json -Compress)
$stale = Invoke-Api -Method PUT -Uri "$apiEndpoint/data" -Token $tokenMain -Body (@{ data = '{"n":2}'; version = 0 } | ConvertTo-Json -Compress)
Test-Case '7. Stale version -> 409' (($first.StatusCode -eq 200) -and ($stale.StatusCode -eq 409)) "first=$($first.StatusCode) stale=$($stale.StatusCode)"

# 8. Oversize payload -> 413
$big = 'x' * (310 * 1024)
$r = Invoke-Api -Method PUT -Uri "$apiEndpoint/data" -Token $tokenMain -Body (@{ data = $big; version = 0 } | ConvertTo-Json -Compress)
Test-Case '8. Oversize -> 413' ($r.StatusCode -eq 413) "status=$($r.StatusCode)"

# 9. Direct execute-api access (no x-origin-verify) -> 403
$r = Invoke-Api -Method GET -Uri "$directBase/api/data" -Token $tokenMain
Test-Case '9. Direct execute-api -> 403' ($r.StatusCode -eq 403) "status=$($r.StatusCode)"

# 10 & 11. Lifecycle + internal-id stability
$userDel = New-LineUserId
$tokenA = Get-AppToken $userDel
$tokenB = Get-AppToken $userDel
$subA = Get-JwtSub $tokenA
$subB = Get-JwtSub $tokenB

$get1 = Invoke-Api -Method GET -Uri "$apiEndpoint/data" -Token $tokenA
$put1 = Invoke-Api -Method PUT -Uri "$apiEndpoint/data" -Token $tokenA -Body (@{ data = '{"hello":"world"}'; version = 0 } | ConvertTo-Json -Compress)
$get2 = Invoke-Api -Method GET -Uri "$apiEndpoint/data" -Token $tokenA
$del = Invoke-Api -Method DELETE -Uri "$apiEndpoint/account" -Token $tokenA
$get3 = Invoke-Api -Method GET -Uri "$apiEndpoint/data" -Token $tokenA

$lifecycleOk = ($get1.StatusCode -eq 204) -and ($put1.StatusCode -eq 200) -and ($get2.StatusCode -eq 200) -and ($del.StatusCode -eq 204) -and ($get3.StatusCode -eq 204)
Test-Case '10. Lifecycle (login/get/put/delete)' $lifecycleOk "get1=$($get1.StatusCode) put=$($put1.StatusCode) get2=$($get2.StatusCode) del=$($del.StatusCode) get3=$($get3.StatusCode)"

$tokenC = Get-AppToken $userDel
$subC = Get-JwtSub $tokenC
Test-Case '11. Internal id stable, new after delete' (($subA -eq $subB) -and ($subC -ne $subA)) "stable=$($subA -eq $subB) reissued=$($subC -ne $subA)"

# Cleanup the accounts this run created.
Invoke-Api -Method DELETE -Uri "$apiEndpoint/account" -Token $tokenMain | Out-Null
Invoke-Api -Method DELETE -Uri "$apiEndpoint/account" -Token $tokenC | Out-Null

#--------------------------------------------------------------------------------
# Report
#--------------------------------------------------------------------------------

Write-Host ''
$results | Format-Table -AutoSize | Out-String | Write-Host

$failed = @($results | Where-Object { $_.Result -eq 'FAIL' })
if ($failed.Count -gt 0) {
    Write-Host "$($failed.Count) test(s) failed." -ForegroundColor Red
    exit 1
}
Write-Host 'All acceptance tests passed.' -ForegroundColor Green
