# 構築と開発

## 🔧 テンプレートから新規プロジェクトを作る

置き換える対象は 2 系統。

| 系統 | 現在の値 | 使われる場所 |
|---|---|---|
| プロジェクト名（PascalCase） | `Template` | 名前空間・プロジェクト名・ディレクトリ名・アセンブリ名・**Lambda ハンドラー名** |
| デプロイ名（kebab-case） | `template-aws-line-miniapp` | CloudFormation スタック名・S3 名の一部 |

リポジトリルートで実行（先頭 2 行を自分の値に変える）:

```powershell
$Name   = 'Acme'              # PascalCase。名前空間・アセンブリ名になる
$Deploy = 'acme-miniapp'      # kebab-case。CloudFormation スタック名になる

# 1. ファイル内容を置換（.git と成果物ディレクトリは除外）
Get-ChildItem -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\(\.git|bin|obj|publish|publish-api|cdk\.out|node_modules)\\' } |
    ForEach-Object {
        $body = Get-Content $_.FullName -Raw
        $new = $body.Replace('Template.', "$Name.").Replace('template-aws-line-miniapp', $Deploy)
        if ($new -ne $body) { Set-Content $_.FullName $new -NoNewline }
    }

# 2. プロジェクトファイルとディレクトリをリネーム
foreach ($p in 'Backend', 'Frontend', 'IaC') {
    Rename-Item "Template.$p/Template.$p.csproj" "$Name.$p.csproj"
    Rename-Item "Template.$p" "$Name.$p"
}
Get-ChildItem -Filter 'Template.*.slnx' | ForEach-Object { Rename-Item $_.FullName ($_.Name -replace '^Template', $Name) }

# 3. 確認
dotnet build
```

> ⚠️ 置換は必ず**ドット付きの `Template.`** で行う（`Template` 単体だと本文の「テンプレート」等を巻き込む）。
> ⚠️ 最も事故るのは `Template.IaC/ApiConstruct.cs` の **Lambda ハンドラー名**。文字列がずれても `cdk deploy` は成功し、**API 呼び出しだけが実行時に 500** になる。上の置換で直るが、手作業リネーム時はここを最初に疑う。

## 🚀 セットアップ（初回構築）

### 必要なもの

- .NET SDK 10.0+
- Node.js 20+（CDK CLI 実行用）
- AWS CLI v2（認証情報設定済み）
- PowerShell 7+

### 事前設定

`Template.IaC/cdk.json` の各環境の値を設定する。

| キー | 内容 |
|---|---|
| `lineChannelId` | LINE ミニアプリ / LINE ログインチャネルの ID（ID トークンの `aud` 検証に使う） |
| `liffId` | デプロイ先（CloudFront）を endpoint URL に設定した LIFF アプリの ID |
| `liffIdLocal` | ローカル開発用（`https://localhost:5250`）の LIFF アプリの ID（dev のみ） |
| `allowLocalhost` | dev は `true`（HTTP API に localhost CORS を付与） |
| `testJwks` | dev のみ `true` 可（テスト用 JWKS を使い、実機なしで検証可能にする）。**prod で true にすると synth が失敗する** |

> 同梱の `dev` は自動検証がそのまま通るダミー値（`lineChannelId: "2000000001"` 等）が入っている。実機で使うときは実値に置き換える。

### 構築手順

```powershell
# 1. Lambda 成果物を用意（CDK がアセットとして取り込む）
./scripts/deploy-api.ps1

# 2. インフラをデプロイ
cd Template.IaC
npx --yes aws-cdk@latest bootstrap        # アカウント×リージョンで初回のみ
npx --yes aws-cdk@latest deploy -c env=dev --require-approval never --outputs-file ../cdk-outputs.dev.json
cd ..

# 3. JWT 署名鍵を投入（冪等。既に本物の鍵があればスキップ）
./scripts/init-jwt-key.ps1 -Env dev

# 4. アプリをビルドして配信
./scripts/deploy-app.ps1 -Env dev

# 5. (自動検証) 実機・LINE 設定なしで受け入れテストを実行
./scripts/test-acceptance.ps1 -Env dev

# 6. (実機検証) LINE Developers で LIFF エンドポイント URL を CloudFront ドメインへ変更し、
#    LINE アプリから LIFF URL を開く
```

最初のマイルストーンは「`POST /api/auth/line` の疎通」。`testJwks` により**実機前に**確認できる。

## ✅ 受け入れテスト（`test-acceptance.ps1`）

dev + `testJwks=true` 前提。スクリプトが P-256 テスト鍵を生成し、その公開鍵を**テスト用 JWKS** としてアプリバケットに配置、秘密鍵で LINE 形式の ID トークンを偽造して、`POST /api/auth/line` 以降の全経路を実 AWS 上で検証する（テスト鍵は本物の LINE 鍵ではなく、dev が `LINE_JWKS_URL` をこの JWKS に向けているからのみ通る）。

| # | テスト | 期待 |
|---|---|---|
| 1 | トークンなしで `GET /api/data` | 401 |
| 2 | `?userId=...` は無視される（API に受け口がない） | パラメータ有無で同じ結果 |
| 3 | 自前 JWT の `sub` を改ざん | 401（署名不一致） |
| 4 | `alg=none` の JWT | 401 |
| 5 | 期限切れ LINE ID トークンで交換 | 401（exp 検証） |
| 6 | `aud` が別チャネルの LINE トークンで交換 | 401 |
| 7 | 古い version で `PUT` | 409 |
| 8 | 300KB 超の `PUT` | 413 |
| 9 | execute-api へ直接アクセス（CloudFront 迂回） | 403（`x-origin-verify` なし） |
| 10 | ライフサイクル（交換→204→PUT→GET→退会→204） | 全 PASS |
| 11 | 同一 LINE ユーザーは同じ internalUserId、退会後は新規発行 | PASS |

**検証実績（2026-08-30）**: dev 環境に一度デプロイし、上記 11 項目すべて PASS を確認、フロント配信（CSP・WASM ブート）も確認したうえで**削除済み**。同日の AmazonLambdaExtension への実装変換後にも再デプロイして全 11 項目 PASS を再確認し、再び**削除済み**。AWS 上にスタックは存在しない。

## 🧹 削除（片付け）

```powershell
cd Template.IaC
npx --yes aws-cdk@latest destroy -c env=dev --force
cd ..
```

dev は全リソース DESTROY 指定のため原則残骸なし。ただし CDK 内蔵のバケット空化 Lambda の LogGroup（`/aws/lambda/{スタック名}-CustomS3AutoDeleteObject-*`）だけは CloudFormation 管理外のため残ることがある（空・無課金）。気になる場合は手動削除:

```powershell
aws logs describe-log-groups --query "logGroups[?contains(logGroupName,'template-aws-line-miniapp')].logGroupName" --output text |
    ForEach-Object { $_ -split '\s+' } | Where-Object { $_ } |
    ForEach-Object { aws logs delete-log-group --log-group-name $_ }
```

**prod** はバケット・DynamoDB・Secret が RETAIN のため、スタック削除後に手動削除が必要（データ保全のための意図的な設計）。

残骸確認:

```powershell
aws cloudformation describe-stacks --stack-name template-aws-line-miniapp-dev   # 存在しないエラーが正
aws s3api list-buckets --query "Buckets[?contains(Name,'template-aws-line-miniapp')].Name"
aws dynamodb list-tables --query "TableNames[?contains(@,'template-aws-line-miniapp')]"
aws secretsmanager list-secrets --include-planned-deletion --query "SecretList[?contains(Name,'SecretJwtSigningKey')].Name"
```

## 📁 プログラム構成

```
template-aws-line-miniapp/
├── SPEC.md                                  ← 実装仕様（v2 仕様からの写像。本 README の上流）
├── Template.Backend/                        ← Lambda (net10.0, 管理 dotnet10)
│   ├── ServiceResolver.cs                   ← DI 登録（[ServiceResolver] から参照）
│   ├── Functions/MiniAppFunction.cs         ← [Lambda] + [HttpApi]×4（AuthLogin/DataGet/DataPut/AccountDelete）
│   ├── Filters/OriginVerifyFilter.cs        ← x-origin-verify 検証（ILambdaFilter・クラス共通）
│   ├── Application/BackendOptions.cs        ← 環境変数の読み取り
│   ├── Services/                            ← LineTokenValidator / SigningKeyProvider / JwtIssuer / OwnTokenValidator / UserRepository
│   └── Models/                              ← Contracts（AuthLogin/DataGet/DataPut。DataAnnotations 検証付き）
├── Template.Frontend/                       ← Blazor WebAssembly (net10.0)
│   ├── Services/                            ← LiffService / TokenStore（メモリ保持）/ ApiClient（401時1回だけ再交換）
│   ├── Components/Pages/                    ← Home / Data / Account / NotFound
│   └── wwwroot/js/liff-interop.js           ← v2 §3.2 のラッパー（外部ファイル。CSP のため）
├── Template.IaC/                            ← AWS CDK (C#)
│   ├── Infrastructure.cs                    ← スタック本体 + Outputs
│   ├── HostingConstruct.cs                  ← S3 + CloudFront（/api/* + x-origin-verify + CSP）
│   ├── ApiConstruct.cs                      ← HTTP API + Lambda ×4 + IAM（Scan/Query 明示 Deny）
│   ├── DataConstruct.cs / SecretConstruct.cs
│   └── EnvironmentConfig.cs / cdk.json / Program.cs
├── scripts/                                 ← 下記
└── README.md / AGENTS.md / Analyzers.ruleset / Directory.Build.*
```

### scripts 一覧

| スクリプト | 用途 |
|---|---|
| `deploy-api.ps1` | Template.Backend を `publish-api/` へ publish（**cdk deploy の前**に必須。destroy 時も必要） |
| `init-jwt-key.ps1` | ES256 鍵ペアを生成し Secrets Manager へ投入（冪等。`-Force` で再生成＝既存 JWT 全失効） |
| `update-appsettings.ps1` | ローカル開発用 `appsettings.Development.json` を生成（LiffId は local 用） |
| `deploy-app.ps1` | Template.Frontend を publish → S3 sync → immutable キャッシュ昇格 → Brotli 差し替え → invalidation |
| `test-acceptance.ps1` | 上記の自動受け入れテスト（dev + testJwks 前提） |
| `common.ps1` | 共通処理（cdk outputs 読み取り等）。直接実行しない |

## 💻 ローカル開発

```powershell
./scripts/update-appsettings.ps1 -Env dev
dotnet run --project Template.Frontend       # https://localhost:5250
```

- LIFF は https 必須のため、ローカル用 LIFF アプリのエンドポイントを `https://localhost:5250` に設定しておく（`dotnet dev-certs https --trust` が前提）。
- API は dev スタックの CloudFront `/api/*` を呼ぶ（クロスオリジン。dev のみ HTTP API に localhost CORS を設定済み）。CloudFront 経由なので `x-origin-verify` は付与され、テスト 9 の直接アクセス遮断とは両立する。
