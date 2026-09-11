# LINEミニアプリ / ユーザ別JSONデータ管理サービス 実装仕様書(サンプル実装版)

**版数**: 3.0
**作成日**: 2026-08-30
**ベース仕様**: `D:\GitWorkspace\_Work-Host-Line\line-miniapp-spec-v2.md`(v2.0)
**構造の参照元**: `D:\GitHubTemplate\template-aws-s3-wasm`
**LIFF 実装の参照元**: `D:\GitWorkspace\_Work-Host-Line`(LIFF 実験プロジェクト)

---

## 0. 本書の位置づけ

v2 仕様書は設計思想(クライアントを一切信頼しない / 信頼の連鎖 / 認可をキー構造にする)と要件を定めたもの。本書はそれを **template-aws-s3-wasm と同一のプロジェクト構造・ビルド/デプロイ手順・コーディング規約**に落とし込んだ、サンプル実装のための仕様である。

- v2 の設計原則(§1)、認証アーキテクチャ(§2〜3)、データモデル(§6)、API 仕様(§7)、セキュリティ仕様(§8)、受け入れテスト(§14)は**全て継承**する。本書で再掲しない詳細は v2 を正とする。
- v2 から変更した点は §1 に集約し、以降の章は変更後の姿で記述する。
- template-aws-s3-wasm の Cognito / S3 データバケット / Identity Pool は**本構成には存在しない**。認証は LINE(LIFF)のみ、データは DynamoDB のみ。

---

## 1. v2 からの変更点(サマリ)

| # | 項目 | v2 | 本書 | 理由 |
|---|---|---|---|---|
| 1 | API の入口 | Lambda Function URL(AWS_IAM + CloudFront OAC) | **API Gateway HTTP API + CloudFront `/api/*` ビヘイビア** + オリジン検証ヘッダー | template-aws-s3-wasm と構造を揃える(ルーティングを CDK に集約、Lambda Annotations の関数分割)。JWT 検証はどちらでも Lambda 内で行うため信頼の連鎖は不変。直接アクセス遮断は CloudFront が付与するカスタムヘッダーの検証で代替(§6.1) |
| 2 | Lambda ランタイム | .NET 10 Native AOT / 512MB | **マネージド dotnet10 ランタイム / 256MB** | Windows から Linux 向け AOT ビルドには実質 Docker が必要で、テンプレートの「.NET SDK だけでビルドできる」前提が崩れる。テンプレート強化方針でも Native AOT は不採用。コールドスタート約 700ms はサンプルとして許容 |
| 3 | WAF(レートベースルール) | 必須 | **サンプルでは見送り**(拡張ポイントとして文書化) | CloudFront 用 WebACL は us-east-1 への別スタックが必要になり、サンプルの規模に見合わない。代替として HTTP API ステージに控えめなスロットリングを設定(§9.4)。WAF 層は「誰が呼んだか」を守る層ではないため、認証設計への影響はない |
| 4 | liffInterop の置き場所 | `index.html` にインライン記述 | **外部ファイル `wwwroot/js/liff-interop.js`** | CSP `script-src 'self'`(インラインスクリプト禁止)を template-aws-s3-wasm から継承するため |
| 5 | LIFF の C# ラッパー | (実験では LineDC.Liff 1.0.0 を使用) | **自前の最小 JS interop**(`LiffService` + `liff-interop.js`) | v2 §3.2 の「期限チェック付きラッパー(期限切れ時に logout→login)」を確実に実装するため。LineDC.Liff は古く、この制御を内包しない |
| 6 | フロント技術(v2 §11 の要判断) | 未決 | **案C: Blazor WASM + 最適化で確定** | 参照 2 プロジェクトとも Blazor WASM であり、template-aws-s3-wasm の最適化済みデプロイパイプライン(WASM AOT / immutable キャッシュ / Brotli / スプラッシュ)をそのまま流用できる |
| 7 | PITR | ON | **prod のみ ON**(dev は OFF) | dev は使い捨て環境(スタック削除で全消去)のため保険が不要。prod は v2 どおり ON |
| 8 | DynamoDB テーブル名 | `user-auth` / `user-data` 固定 | **CDK 自動命名 + Lambda 環境変数で連携** | dev/prod の並存と削除の容易さのため。テンプレートのバケット命名と同じ方針 |
| 9 | 自前 JWT のクレーム | `sub` = internalUserId | `sub` に加え **`line_sub` = lineUserId を追加** | `DELETE /api/account` が `user-auth`(PK: LINE#\<lineUserId\>)と `user-data`(PK: USER#\<internalUserId\>)の両方を削除するため。GSI を張らずに逆引きを不要化する |
| 10 | internalUserId | ULID | **`usr_` + Guid.CreateVersion7()("N"書式)** | .NET 標準のみで時系列ソート可能な一意 ID を満たし、依存パッケージを増やさない |
| 11 | JWKS の取得先 | LINE 固定 | **環境変数 `LINE_JWKS_URL`(既定は LINE)。dev のみテスト用 JWKS へ差し替え可** | LINE 実機なしで `/api/auth/line` 以降の全経路を AWS 上で自動検証するため(§14)。prod では設定自体を生成しない(§6.2) |
| 12 | 秘密鍵の保管 | Secrets Manager / KMS | **Secrets Manager**(ES256 PEM。`scripts/init-jwt-key.ps1` で投入) | KMS 非対称鍵は JWT ライブラリと直結できず署名処理の自前実装が増える。サンプルでは PEM + 標準ライブラリを選択 |
| 13 | ログ保持期間 | 30日 | dev 1週間 / prod 1か月 | template-aws-s3-wasm の設定を踏襲 |
| 14 | Lambda ハンドラーの実装形態 | (v2 は ASP.NET Core 風の記述) | **AmazonLambdaExtension**(自作ソースジェネレーター、NuGet 2.0.0-beta4)の `[Lambda]` + `[HttpApi]` + パラメーターバインディング + フィルターパイプライン | 2026-08-30 変更。当初は Amazon.Lambda.Annotations で実装(バックアップ: `template-aws-line-miniapp-backup`)。オリジン検証はクラス共通の `ILambdaFilter` に、Body/Header の取り出しは `[FromBody]`/`[FromHeader]` に置換。JWT 検証は設計どおりハンドラー内に残す |

**変更していないもの(v2 を正とする)**: 2トークン方式とトークンの役割分担 / 期限切れ 4 状態と対処(§3) / DynamoDB 2 テーブルのキー設計と楽観ロック / API のシグネチャに userId を持たせない原則 / 検証項目チェックリスト(`alg` 固定・`aud` 検証ほか) / 401 の理由を返さない / JWT の localStorage 保存禁止 / `Scan`・`Query` の IAM 明示 Deny / プロバイダー設計の不可逆性。

---

## 2. 全体構成

```
┌──────────────────────────────────────────┐
│ LINEアプリ(LIFFブラウザ)/ 外部ブラウザ      │
│  Blazor WASM (.NET 10) + LIFF SDK (CDN)   │
│  liff-interop.js → LiffService (JS interop)│
└─────────────┬─────────────────────────────┘
              │ HTTPS
              ▼
       ┌──────────────┐
       │  CloudFront  │  OAC / セキュリティヘッダー / SPAフォールバック
       └──┬────────┬──┘
    /*    │        │  /api/*(キャッシュ無効・オリジン検証ヘッダー付与)
          ▼        ▼
     ┌────────┐  ┌──────────────────────┐
     │   S3   │  │ API Gateway HTTP API │
     │ (OAC)  │  │  └ Lambda ×4         │
     │ アプリ  │  │    (dotnet10管理RT)   │
     └────────┘  └──────┬───────────────┘
                        ▼
                 ┌──────────────┐   ┌────────────────┐
                 │  DynamoDB    │   │ Secrets Manager │
                 │ auth / data  │   │ JWT署名鍵(ES256) │
                 └──────────────┘   └────────────────┘

外部: LINE Platform(JWKS: api.line.me/oauth2/v2.1/certs)
```

S3 は**静的ファイル配信のみ**(v2 どおり)。ユーザーデータ用バケットは作らない。

### AWS リソース一覧

| リソース | 用途 | 要点 |
|---|---|---|
| S3(アプリ) | Blazor publish 出力の配信元 | パブリックアクセス全ブロック / OAC のみ / dev は autoDelete |
| CloudFront | アプリ配信 + API 前段 | OAC / HTTPS 強制 / 403・404→`/index.html`(200) / セキュリティヘッダー+CSP / PriceClass 200 / `/api/*` はキャッシュ無効・`x-origin-verify` ヘッダー付与 |
| API Gateway HTTP API | API の入口 | オーソライザーなし(JWT 検証は Lambda 内)。dev のみ localhost CORS。ステージスロットリング |
| Lambda ×4 | auth-line / data-get / data-put / account-delete | マネージド dotnet10 / 256MB / 10秒 / 1 publish 成果物を共有 |
| DynamoDB ×2 | `user-auth` / `user-data` 相当 | オンデマンド / PITR は prod のみ / dev は DESTROY |
| Secrets Manager | JWT 署名鍵(ES256 秘密鍵 PEM) | CDK は器のみ作成、値はスクリプトで投入。Lambda は 15 分キャッシュ |
| CloudWatch Logs | Lambda ログ | dev 1週間 / prod 1か月 |

### LINE 側(IaC 管理外・手動)

| 項目 | 値 |
|---|---|
| プロバイダー | 1つに集約(**不可逆**。v2 §4.1 の警告をそのまま適用) |
| チャネル | LINEミニアプリチャネル(検証は既存の LINEログイン+LIFF チャネルでも可。SDK・トークン仕様は同一) |
| Scope | `openid`, `profile` |
| エンドポイント URL | `https://<CloudFrontドメイン>/`(デプロイ後に手動設定) |
| テスト | 開発用ミニアプリに Tester 登録(v2 §4.4) |

---

## 3. プロジェクト構成

ディレクトリ名は `template-aws-line-miniapp`、プロジェクト接頭辞は他テンプレートと同じ `Template.`、CloudFormation スタック名は `template-aws-line-miniapp-{env}`。規約ファイル(`.editorconfig` / `Analyzers.ruleset` / `Directory.Build.props` / `Directory.Build.targets` / `AGENTS.md` / `CLAUDE.md`)は template-aws-s3-wasm からそのまま複製する。

```
template-aws-line-miniapp/
├── .editorconfig / Analyzers.ruleset / Directory.Build.props / Directory.Build.targets
├── AGENTS.md / CLAUDE.md
├── Template.AWS.Line.MiniApp.slnx
├── SPEC.md                                  ← 本書
│
├── Template.Backend/                        ← Lambda (net10.0, マネージド dotnet10)
│   ├── ServiceResolver.cs                   ← DI 登録([ServiceResolver] から参照される)
│   ├── Functions/
│   │   └── MiniAppFunction.cs               ← [Lambda] クラス。[HttpApi] メソッド×4
│   │       (Login / GetData / PutData / DeleteAccount → {Method}_Handler が生成される)
│   ├── Filters/
│   │   └── OriginVerifyFilter.cs            ← x-origin-verify 検証(ILambdaFilter、クラス共通・§6.1)
│   ├── Application/
│   │   └── BackendOptions.cs                ← 環境変数の読み取り(§8.3)
│   ├── Services/
│   │   ├── LineTokenValidator.cs            ← LINE IDトークン検証(JWKS 15分キャッシュ)
│   │   ├── SigningKeyProvider.cs            ← 署名鍵の取得・キャッシュ(Secrets Manager)
│   │   ├── JwtIssuer.cs                     ← 自前JWT発行
│   │   ├── OwnTokenValidator.cs             ← 自前JWT検証(Bearer 解析 + 公開鍵検証)
│   │   └── UserRepository.cs                ← DynamoDB アクセス(GetItem/PutItem/Transact)
│   ├── Models/                              ← LoginRequest/LoginResponse/DataResponse/PutRequest 等
│   ├── FunctionSerializerContext.cs / Assembly.cs / GlobalUsing.cs / GlobalSuppressions.cs
│   └── Template.Backend.csproj              ← AmazonLambdaExtension 参照(ジェネレーター同梱)
│
├── Template.Frontend/                       ← Blazor WebAssembly (net10.0)
│   ├── Program.cs
│   ├── Application/ViewHelper.cs
│   ├── Services/
│   │   ├── LiffService.cs                   ← liff-interop.js の JS interop ラッパー
│   │   ├── TokenStore.cs                    ← 自前JWTのメモリ保持(localStorage 禁止)
│   │   └── ApiClient.cs                     ← トークン確保 + 401時1回だけ再交換リトライ(§7.4)
│   ├── Components/
│   │   ├── App.razor / _Imports.razor
│   │   ├── Layout/MainLayout.razor(.cs)
│   │   └── Pages/
│   │       ├── Home.razor(.cs)              ← LIFF初期化・プロフィール・トークン状態(実験プロジェクト相当)
│   │       ├── Data.razor(.cs)              ← JSONデータの取得/編集/保存・409/413 のUI
│   │       ├── Account.razor(.cs)           ← 退会(確認 → DELETE → logout)
│   │       └── NotFound.razor
│   ├── Models/ / Settings/AppSetting.cs
│   └── wwwroot/
│       ├── index.html                       ← スプラッシュ + LIFF SDK(CDN) + liff-interop.js
│       ├── js/liff-interop.js               ← v2 §3.2 のラッパー(外部ファイル化)
│       ├── appsettings.json / appsettings.Development.json(生成)
│       └── css/app.css
│
├── Template.IaC/                            ← AWS CDK (C#)
│   ├── cdk.json                             ← 環境別 context(§9.5)
│   ├── Program.cs / EnvironmentConfig.cs
│   ├── Infrastructure.cs                    ← スタック本体 + Outputs
│   ├── HostingConstruct.cs                  ← アプリバケット + CloudFront(/api/* ビヘイビア)
│   ├── DataConstruct.cs                     ← DynamoDB 2テーブル
│   ├── SecretConstruct.cs                   ← JWT署名鍵の Secret(器のみ)
│   └── ApiConstruct.cs                      ← HTTP API + Lambda ×4(2段構成はテンプレート踏襲)
│
├── scripts/
│   ├── common.ps1                           ← cdk outputs 読み取り等(テンプレート踏襲)
│   ├── deploy-api.ps1                       ← Backend publish(cdk deploy の前提)
│   ├── deploy-app.ps1                       ← Frontend publish → S3 sync → invalidation
│   ├── update-appsettings.ps1               ← ローカル開発用 appsettings.Development.json 生成
│   ├── init-jwt-key.ps1                     ← ES256 鍵ペア生成 → Secrets Manager へ投入(冪等)
│   └── test-acceptance.ps1                  ← §14 の自動テスト(dev + テストJWKS 前提)
│
└── README.md
```

---

## 4. 認証設計

v2 §1〜3 の 2 トークン方式を全面的に継承する。信頼の連鎖・4 状態の対処・「リトライは 1 回まで」・「401 の理由を返さない」は全て有効。

### 4.1 トークン仕様

| | LINE ID トークン | 自前 JWT |
|---|---|---|
| 発行者(`iss`) | `https://access.line.me` | `template-aws-line-miniapp` |
| 対象(`aud`) | LINE チャネル ID | `miniapp` |
| 署名 | ES256(LINE 秘密鍵、JWKS で検証) | ES256(Secrets Manager の秘密鍵) |
| 有効期限 | 1時間(LINE 固定) | 7日(`JWT_LIFETIME_DAYS` で変更可) |
| 検証場所 | `POST /api/auth/line` のみ | 全保護 API(Lambda 内共通処理) |
| 保管場所 | LIFF の localStorage(触らない) | **メモリのみ**(`TokenStore`) |

### 4.2 自前 JWT のクレーム

```
iss: "template-aws-line-miniapp"
aud: "miniapp"
sub: internalUserId          … "usr_" + Guid.CreateVersion7()("N"書式)
line_sub: lineUserId         … 退会時の user-auth 削除にのみ使用(ログ出力禁止)
iat / exp: 発行時刻 / +7日
alg: ES256, kid: "primary"
```

### 4.3 検証パラメータ(必須チェックリスト、v2 §7.1 継承)

**LINE ID トークン検証**(`/api/auth/line`): `ValidAlgorithms = [ES256]` / `iss = https://access.line.me` / `aud = 自チャネルID` / `exp` 検証 / ClockSkew 1分 / 鍵は JWKS(15 分キャッシュ)の `kid` 一致要素。

**自前 JWT 検証**(共通): `ValidAlgorithms = [ES256]` / `iss`・`aud` は上表 / `ValidateLifetime = true` / ClockSkew 1分 / 検証には公開鍵のみ使用。

JWT ライブラリは `Microsoft.IdentityModel.JsonWebTokens`(`JsonWebTokenHandler.ValidateTokenAsync`)。

### 4.4 起動シーケンス(v2 §2.3 継承)

```
liff.init → isLoggedIn? no → liff.login(redirectUri: location.href)
         → yes → getFreshIdToken()  ← 期限切れ/残60秒未満なら logout→login(v2 §3.2)
         → POST /api/auth/line { idToken } → 自前JWT(7日)を TokenStore へ
         → 以後の API は Bearer <自前JWT>。LINE ID トークンは使わない
```

ページリロードで TokenStore は消えるが、LIFF の ID トークンキャッシュが有効なら再交換は 1 リクエストで完了する(設計どおりの挙動であり問題ではない)。

---

## 5. データモデル(DynamoDB)

v2 §6 を継承。テーブル名のみ CDK 自動命名とし、Lambda へは環境変数で渡す。

### `user-auth` 相当(論理名 AuthTable)

| 属性 | 型 | 内容 |
|---|---|---|
| `PK` | S | `LINE#<lineUserId>`(パーティションキー) |
| `internalUserId` | S | `usr_...`(Guid v7) |
| `createdAt` | S | ISO8601(UTC) |

### `user-data` 相当(論理名 DataTable)

| 属性 | 型 | 内容 |
|---|---|---|
| `PK` | S | `USER#<internalUserId>`(パーティションキー) |
| `data` | S | JSON 文字列(構造はアプリ任意。サーバはサイズのみ検査) |
| `version` | N | 楽観ロック用連番 |
| `updatedAt` | S | ISO8601(UTC) |

設計方針(ソートキーなし / GSI なし / オンデマンド / `data` は String)は v2 §6.3 のまま。初回ログイン時の内部 ID 発行は `attribute_not_exists(PK)` 条件付き PutItem で行い、条件違反(並行登録)時は再 GetItem で解決する。

---

## 6. API 仕様

エンドポイントと入出力は v2 §7 と同一。実装形態のみ template-aws-s3-wasm 方式(Lambda Annotations + ルーティングは CDK)へ写像する。

| メソッド/パス | 関数 | 認証 | 成功 | エラー |
|---|---|---|---|---|
| POST `/api/auth/line` | AuthLineFunction | 不要(オリジン検証のみ) | 200 `{token, expiresIn}` | 401(ID トークン検証失敗。理由は返さない) |
| GET `/api/data` | DataGetFunction | Bearer(自前JWT) | 200 `{data, version, updatedAt}` / 204(未作成) | 401 |
| PUT `/api/data` | DataPutFunction | Bearer(自前JWT) | 200 `{version}` | 401 / 409(version 不一致) / 413(300KB 超) |
| DELETE `/api/account` | AccountDeleteFunction | Bearer(自前JWT) | 204 | 401 |

- リクエスト/レスポンスに `userId` は一切現れない(v2 §7.3 の要点を継承)。
- PUT のサイズ制限: 100KB 超で警告ログ、300KB 超で 413(v2 §7.4 のコードをそのまま採用)。
- 楽観ロック: `attribute_not_exists(PK) OR #v = :expected` の条件付き PutItem。条件違反は 409。
- 退会: `TransactWriteItems` で `AuthTable(LINE#line_sub)` と `DataTable(USER#sub)` の 2 アイテムを同時削除(データ未作成でも Delete は成功する)。

### 6.1 共通リクエストパイプライン(全関数)

v2 §7.2 のミドルウェアに相当する処理を、AmazonLambdaExtension のフィルターパイプラインとハンドラー先頭の検証で構成する(§8.1)。

1. **オリジン検証**(`OriginVerifyFilter`、クラス共通の `ILambdaFilter`): ヘッダー `x-origin-verify` が CloudFront の設定値と一致しなければ **403**。execute-api エンドポイントへの直接アクセスを CloudFront 経由に限定する(v2 の「Function URL を OAC で保護」の代替。この層は多層防御であり認証ではない — 認証は次段の JWT が担う)
2. **JWT 検証**(auth/line 以外、各ハンドラー先頭で `OwnTokenValidator.ValidateHeaderAsync`): `[FromHeader]` で受けた `Authorization: Bearer` を検証し、`sub` / `line_sub` を取り出す。失敗は一律 **401**(理由の区別を返さない)。フィルターではなくハンドラー内に置くのは、auth/line だけ対象外である点と、「`sub` の出所が検証済みトークンのみ」という要点をコード上で可視に保つため
3. ハンドラー本体は検証済みの `sub` だけを使って DynamoDB キーを組み立てる

### 6.2 JWKS の切り替え(検証支援・dev 限定)

- Lambda は `LINE_JWKS_URL` から検証鍵を取得する。**既定値は `https://api.line.me/oauth2/v2.1/certs`**。
- dev 環境で cdk context `testJwks: true` の場合のみ、`https://<CloudFrontドメイン>/test-jwks.json` に差し替える。`test-acceptance.ps1` がテスト用 ES256 鍵ペアを生成し、公開鍵(JWKS)をアプリバケットへアップロード、秘密鍵で「LINE 形式の ID トークン」(iss/aud/exp を正規値で署名)を偽造して全経路を自動検証する。
- **prod の context にはこのキー自体を定義しない**。EnvironmentConfig は prod で `testJwks` が true なら例外を投げる(構造的に持ち込み不可にする)。

---

## 7. フロントエンド仕様

### 7.1 index.html

template-aws-s3-wasm のスプラッシュ構成を踏襲し、以下を追加する。

```html
<script src="https://static.line-scdn.net/liff/edge/2/sdk.js"></script>
<script src="js/liff-interop.js"></script>
```

実験プロジェクトはバージョン固定(2.22.3)の CDN を使っていたが、LINE 公式が推奨する edge/2(最新 2.x)を使用する。CSP の `script-src` に `https://static.line-scdn.net` を追加する(§10.1)。

### 7.2 js/liff-interop.js(v2 §3.2 のラッパーを外部ファイル化)

```javascript
window.liffInterop = {
  init: async function (liffId) { ... },        // liff.init → 未ログインなら liff.login(redirectUri)
  getFreshIdToken: function () { ... },         // 期限切れ/残60秒未満 → liff.logout()+liff.login()、それ以外は liff.getIDToken()
  getProfile: async function () { ... },        // displayName / pictureUrl / statusMessage
  isInClient: () => liff.isInClient(),
  closeWindow: () => liff.closeWindow()
};
```

- `liff.logout()` を挟まないと期限切れキャッシュが残り続ける(v2 §3.2 の注意)。
- `liff.getDecodedIDToken()` の値は**サーバへ送らない**(表示用途にも使わない。プロフィール表示は `liff.getProfile()`)。

### 7.3 サービス

| クラス | 責務 |
|---|---|
| `LiffService` | liffInterop の型付きラッパー。`InitializeAsync(liffId)` / `GetFreshIdTokenAsync()` / `GetProfileAsync()` |
| `TokenStore` | 自前 JWT と有効期限をメモリ保持(Singleton 相当の Scoped)。localStorage は使わない |
| `ApiClient` | `EnsureTokenAsync()`(未取得なら LIFF → 交換)+ 各 API 呼び出し + 401 時の再交換リトライ(**1回のみ**、v2 §3.3 のコードを踏襲) |

### 7.4 ページ

| ページ | 内容 |
|---|---|
| `Home` | LIFF 初期化、プロフィール(画像/名前/ステータス)、環境情報(OS/言語/isInClient)、トークン状態の表示。実験プロジェクトの Home.razor に相当。**ID トークンやアクセストークンの生値は画面に出さない**(実験コードからの改善) |
| `Data` | `GET /api/data`(204 なら空の初期状態)→ textarea で JSON 編集 → `PUT`。現在 version と更新日時を表示。409 時は「他の端末で更新されています」+ 再読込ボタン(自動マージはしない)。クライアント側でも 300KB 超を事前警告(サーバ検査が正) |
| `Account` | 確認ダイアログ → `DELETE /api/account` → TokenStore クリア → `liff.logout()` → isInClient なら `closeWindow()` |

### 7.5 設定(wwwroot/appsettings.json)

```json
{
  "App": {
    "LiffId": "{LIFF ID}",
    "ApiEndpoint": "https://{distribution}.cloudfront.net/api"
  }
}
```

ここに載る値はすべて公開可能。`appsettings.Production.json` は `deploy-app.ps1` が cdk outputs + context から生成する(テンプレート踏襲)。ローカル開発用の `appsettings.Development.json` は `update-appsettings.ps1` が生成し、**LiffId はローカル用チャネルの値**(`liffIdLocal` context)を使う。

### 7.6 ローカル開発

- LIFF のエンドポイント URL は https 必須のため、ローカル用 LIFF アプリのエンドポイントを `https://localhost:5250` にする(実験プロジェクトで実績のある方式。`dotnet dev-certs https --trust` が前提)。
- API は dev スタックの CloudFront `/api/*` を呼ぶ(クロスオリジンになるため、dev のみ HTTP API に localhost CORS を設定する。§9.4)。

---

## 8. バックエンド実装仕様

### 8.1 実装形態

**AmazonLambdaExtension**(自作ソースジェネレーター。NuGet `AmazonLambdaExtension` 2.0.0-beta4、ジェネレーター同梱)を使用する(§1 変更点 #14)。

- `[Lambda]` + `[ServiceResolver]` + `[Filter<OriginVerifyFilter>]` を付けた単一クラス `MiniAppFunction` に、`[HttpApi(method, path)]` メソッドを4つ定義する
- ジェネレーターが各メソッドの Lambda エントリポイント `{Method}_Handler` を同クラスに生成する。CDK のハンドラー文字列は `Template.Backend::Template.Backend.Functions.MiniAppFunction::{Method}_Handler`
- Body は `[FromBody]`(SG ベースの `IBodySerializer` + DataAnnotations 検証で不正 JSON は 400)、Authorization ヘッダーは `[FromHeader("authorization")] string authorization = ""`(既定値必須。null 許容注釈型はバインディング非対応)で受ける
- `[HttpApi]` のルートテンプレートはドキュメント兼ルートパラメーターバインディング用であり、実ルーティングは従来どおり CDK 側(HTTP API のルート定義)にある
- 全関数が 1 つの publish 成果物を共有する点は従来と同じ。Annotations と異なり `serverless.template` は生成されないため削除ターゲットは不要

### 8.2 DI 登録(ServiceResolver)

| サービス | 生存期間 | 内容 |
|---|---|---|
| `IAmazonDynamoDB` / `IAmazonSecretsManager` | Singleton | ウォーム間で再利用 |
| `LineTokenValidator` | Singleton | JWKS を 15 分キャッシュ(キャッシュ後は外部通信ゼロ) |
| `JwtIssuer` / `OwnTokenValidator` | Singleton | 署名鍵を 15 分キャッシュ。検証は公開鍵のみ |
| `UserRepository` | Singleton | テーブル名は BackendOptions から |
| `TimeProvider` | Singleton | `TimeProvider.System`(テンプレート強化方針に合わせ導入) |

### 8.3 環境変数(CDK が設定)

| 変数 | 内容 |
|---|---|
| `AUTH_TABLE` / `DATA_TABLE` | DynamoDB テーブル名 |
| `LINE_CHANNEL_ID` | ID トークンの `aud` 検証値 |
| `LINE_JWKS_URL` | 既定 `https://api.line.me/oauth2/v2.1/certs`。dev + testJwks 時のみ差し替え |
| `JWT_SECRET_ARN` | 署名鍵 Secret の ARN(ARN 自体は秘密情報ではない) |
| `JWT_ISSUER` / `JWT_AUDIENCE` / `JWT_LIFETIME_DAYS` | `template-aws-line-miniapp` / `miniapp` / `7` |
| `ORIGIN_VERIFY` | CloudFront が付与するヘッダー値(多層防御用。認証には使わない) |

**署名鍵そのものは環境変数に置かない**(v2 §8.4)。`ORIGIN_VERIFY` は CloudFront ディストリビューション設定に平文で載る性質の値であり秘密情報として扱わない。

### 8.4 パッケージ

`AmazonLambdaExtension`(ランタイム + ソースジェネレーター同梱)/ `Amazon.Lambda.APIGatewayEvents` / `Amazon.Lambda.Core` / `Amazon.Lambda.Serialization.SystemTextJson` / `AWSSDK.DynamoDBv2` / `AWSSDK.SecretsManager` / `Microsoft.IdentityModel.JsonWebTokens` / `Microsoft.Extensions.DependencyInjection`

### 8.5 ログ(v2 §8.4 継承)

構造化ログ。`lineUserId` は出力禁止(内部 ID のみ)。例: `{"level":"info","event":"data.read","userId":"usr_...","ts":"..."}`

---

## 9. IaC 仕様(Template.IaC)

### 9.1 構築順

```
Api(本体のみ) → Hosting(/api/* オリジンに API ホストが必要)
             → Data(DynamoDB。依存なし)
             → Secret(依存なし)
             → Api.AddRoutes(テーブル名・Secret ARN・CloudFrontドメイン(testJwks時)が必要)
```

Cognito が消えたため v2 テンプレートより依存関係は単純だが、「API を裸で先に作り、後からルートを足す」2 段構成は踏襲する(CloudFront が API ホスト名を要求するため)。

### 9.2 HostingConstruct

- template-aws-s3-wasm と同一のバケット/ディストリビューション設定(OAC / SPA フォールバック / PriceClass 200 / セキュリティヘッダー)。
- `/api/*` ビヘイビア: キャッシュ無効 / `ALL_VIEWER_EXCEPT_HOST_HEADER` / **`OriginCustomHeaders` で `x-origin-verify: <値>` を付与**。値はスタック単位で決まる擬似乱数文字列(cdk context または Stack ID からの導出)。
- CSP は §10.1 のとおり差し替え。

### 9.3 DataConstruct

| 設定 | dev | prod |
|---|---|---|
| BillingMode | PAY_PER_REQUEST | 同左 |
| PITR | OFF | ON |
| RemovalPolicy | DESTROY | RETAIN |
| 暗号化 | AWS 所有キー(既定) | 同左 |

### 9.4 ApiConstruct

- HTTP API。オーソライザーなし(検証は Lambda 内。§6.1)。
- dev のみ CORS: `AllowOrigins = [https://localhost:5250]`, `AllowHeaders = [authorization, content-type]`, `AllowMethods = [GET, PUT, POST, DELETE]`。prod は CORS 設定なし(CloudFront 同一オリジンのみ)。
- デフォルトステージに `ThrottlingBurstLimit = 100 / ThrottlingRateLimit = 50` を設定(WAF 見送りの代替。コスト増幅の抑止)。
- Lambda ×4: dotnet10 / 256MB / 10秒 / LogGroup(dev 1週間・DESTROY / prod 1か月・RETAIN)。ハンドラー名はソースジェネレーター規約 `Template.Backend::Template.Backend.Functions.{Class}_Handle_Generated::Handle`。
- IAM(実行ロール、v2 §9.2 継承・関数別に最小化):

| 関数 | Allow | 備考 |
|---|---|---|
| AuthLine | `dynamodb:GetItem`, `PutItem`(AuthTable)+ `secretsmanager:GetSecretValue`(署名鍵) | |
| DataGet | `dynamodb:GetItem`(DataTable)+ `GetSecretValue` | 検証は公開鍵だが鍵素材は同一 Secret |
| DataPut | `dynamodb:PutItem`(DataTable)+ `GetSecretValue` | |
| AccountDelete | `dynamodb:DeleteItem`(両テーブル)+ `GetSecretValue` | Transact は Delete 権限で許可される |
| 全関数共通 | **Deny `dynamodb:Scan` / `dynamodb:Query`(Resource: *)** | 事故の構造的防止(v2 の要点) |

### 9.5 cdk.json context

```json
{
  "app": "dotnet run",
  "context": {
    "dev":  { "lineChannelId": "<チャネルID>", "liffId": "<デプロイ用LIFF ID>",
              "liffIdLocal": "<ローカル用LIFF ID>", "allowLocalhost": true, "testJwks": true },
    "prod": { "lineChannelId": "<チャネルID>", "liffId": "<LIFF ID>",
              "allowLocalhost": false }
  }
}
```

### 9.6 Outputs

`CloudFrontDomain` / `DistributionId` / `AppBucketName` / `ApiEndpoint`(CloudFront 経由)/ `DirectApiEndpoint`(execute-api。テスト9用)/ `AuthTableName` / `DataTableName` / `JwtSecretArn` / `LineChannelId`

---

## 10. セキュリティ仕様

v2 §8(攻撃シナリオ別防御・レイヤー役割・禁止事項)を全面継承。CORS は認証ではない、WASM 改造では何も起きない、という原則も同じ。以下は本構成での具体化。

### 10.1 CSP(CloudFront ResponseHeadersPolicy)

```
default-src 'self';
connect-src 'self' https://api.line.me;
script-src 'self' 'wasm-unsafe-eval' https://static.line-scdn.net;
style-src 'self' 'unsafe-inline';
img-src 'self' data: https://profile.line-scdn.net;
base-uri 'self'; form-action 'self'; frame-ancestors 'none'; object-src 'none'
```

- Cognito / S3 ワイルドカードは削除(存在しないため)。API は同一オリジン(`'self'`)。
- `https://api.line.me`: LIFF SDK の init / getProfile 等の XHR 先。
- `https://static.line-scdn.net`: LIFF SDK 本体。
- `https://profile.line-scdn.net`: プロフィール画像(実験プロジェクトで表示実績のあるホスト)。
- **LIFF ブラウザ実機で CSP violation が出た場合はホストを追記する**(§14 の実機確認項目。LIFF SDK の通信先は公式に網羅列挙されていないため、初回実機テストで確定させる)。

### 10.2 攻撃シナリオ表の差分(v2 §8.1 に対する追加・置換)

| 攻撃 | 結果 | 止まる場所 |
|---|---|---|
| execute-api へ直接アクセス(CloudFront 迂回) | 403 | `x-origin-verify` 不一致(多層防御。JWT 検証は依然有効) |
| `x-origin-verify` を知る攻撃者が直接アクセス | 401(トークンなし) | JWT 検証 — **この層が突破されても認証は破れない** |
| テスト JWKS を prod へ持ち込む | 不可能 | prod context にキーが存在せず、EnvironmentConfig が検出時に例外 |

### 10.3 禁止事項(v2 §8.3 継承 + 追加)

- `liff.getDecodedIDToken()` の結果をサーバへ送らない
- クライアント申告の `userId` を信用しない(API に受け口自体がない)
- 自前 JWT を localStorage / sessionStorage に保存しない(メモリのみ)
- `lineUserId` をログへ出力しない(`line_sub` クレームはログ禁止対象)
- 生トークン(ID トークン / 自前 JWT / アクセストークン)を画面に表示しない

---

## 11. LINE チャネル設定と検証環境

### 11.1 本番想定(v2 §4 継承)

プロバイダー 1 つに集約(不可逆)/ LINEミニアプリチャネル / Scope `openid` `profile` / 未認証ミニアプリとして公開 → 必要になったら審査申請。

### 11.2 サンプル検証(本書での前提)

- **実機検証には `_Work-Host-Line` で使用中の既存チャネル(LINEログイン + LIFF)を再利用する**(ミニアプリチャネルと ID トークンの仕様は同一)。LIFF ID は同プロジェクト `Program.cs` に 2 つ(ローカル用 / デプロイ用)存在し、それぞれ `liffIdLocal` / `liffId` context に対応する。
- デプロイ後、LIFF アプリのエンドポイント URL を CloudFront ドメインへ手動更新する(LINE Developers コンソール)。
- LINE を使わない自動検証(§14 の大部分)は testJwks 機構により**実機・チャネル設定なしで完結**する。

---

## 12. スクリプト仕様

すべて PowerShell、リポジトリルートから実行(テンプレート踏襲)。`common.ps1` は cdk outputs(`cdk-outputs.{env}.json`)の読み取りを共通化する。

| スクリプト | 内容 |
|---|---|
| `deploy-api.ps1` | `dotnet publish Template.Backend -c Release -o publish-api`。**cdk deploy の前に必須**(destroy 時も成果物が必要、というテンプレートの罠を README に継承) |
| `init-jwt-key.ps1` | `[System.Security.Cryptography.ECDsa]::Create(nistP256)` で鍵ペア生成 → PKCS#8 PEM を Secrets Manager へ `put-secret-value`。**既に値があればスキップ(冪等)**。`-Force` で再生成(全ユーザーの既存 JWT が失効する旨を警告) |
| `update-appsettings.ps1` | outputs + context から `appsettings.Development.json` 生成(LiffId は `liffIdLocal`) |
| `deploy-app.ps1` | `appsettings.Production.json` 生成 → publish(出力先を毎回削除)→ S3 sync(.br/.gz 除外・no-cache)→ `_framework` を immutable 昇格 + Content-Type 固定 → 10MB 超の .br 上書き → invalidation。**テンプレートの手順をそのまま流用** |
| `test-acceptance.ps1` | §14 の自動テスト一式。dev + testJwks 前提。テスト鍵生成 → `test-jwks.json` を S3 へ配置 + invalidation → 偽造 ID トークンで全シナリオを実行し PASS/FAIL 表を出力 |

---

## 13. 構築手順

```powershell
# 0. cdk.json の lineChannelId / liffId / liffIdLocal を実値に設定(検証だけなら dummy 可)
# 1. Lambda 成果物
./scripts/deploy-api.ps1
# 2. インフラ
cd Template.IaC
npx --yes aws-cdk@latest bootstrap          # アカウント×リージョン初回のみ
npx --yes aws-cdk@latest deploy -c env=dev --outputs-file ../cdk-outputs.dev.json
cd ..
# 3. JWT 署名鍵の投入
./scripts/init-jwt-key.ps1 -Env dev
# 4. フロント配信
./scripts/deploy-app.ps1 -Env dev
# 5. (自動検証) ./scripts/test-acceptance.ps1 -Env dev
# 6. (実機検証) LINE Developers で LIFF エンドポイント URL を CloudFrontDomain へ変更し、
#    LINE アプリから LIFF URL を開く
```

最初のマイルストーンは v2 §12 のとおり「`POST /api/auth/line` の疎通」。testJwks により実機前に確認できる。

---

## 14. 受け入れテスト(v2 §14 の写像)

### 自動(`test-acceptance.ps1`。dev + testJwks)

| # | テスト | 期待 |
|---|---|---|
| 1 | トークンなしで `GET /api/data` | 401 |
| 2 | 偽造ユーザー A の正規トークンで `GET /api/data?userId=<Bの内部ID>` | A のデータが返る(パラメータ無視) |
| 3 | A の JWT の `sub` を書き換え(署名再計算なし) | 401 |
| 4 | `{"alg":"none"}` に改変した JWT | 401 |
| 5 | 期限切れ JWT(exp を過去にして正規署名) | 401 |
| 6 | `aud` が別チャネル ID の ID トークンで `/api/auth/line` | 401 |
| 7 | 同一ユーザーで古い version の `PUT /api/data` | 409 |
| 8 | 300KB 超の data を `PUT` | 413 |
| 9 | `DirectApiEndpoint`(execute-api)へ直接アクセス | 403(オリジン検証ヘッダーなし) |
| 10 | 正常系一式: auth/line → 204 → PUT(v0) → GET → PUT(v1) → DELETE /account → GET が 401/再ログインで 204 | 全 PASS |
| 11 | 再ログインで同じ lineUserId に同じ internalUserId が返る(退会後は新規発行) | PASS |

### 実機(LINE アプリ / 手動)

| # | テスト | 確認点 |
|---|---|---|
| M1 | LIFF から起動 → プロフィール表示 → データ保存/再読込 | E2E 疎通。CSP violation がコンソールに出ないこと(出たら §10.1 に追記) |
| M2 | 1 時間放置後の操作 | 自前 JWT で継続動作(LINE トークン切れの影響なし。v2 状態②) |
| M3 | `liff.logout()` 経由の再ログイン復帰 | リダイレクトでクエリ・ハッシュが落ちないか(v2 §15 の未決事項) |
| M4 | 2 台(スマホ + PC の外部ブラウザ)で同時編集 | 片方が 409 になり、再読込で復旧できる |

---

## 15. AWS 検証計画と後片付け

> **実施済み（2026-08-30）**: dev 環境(ap-northeast-1)へデプロイし、`test-acceptance.ps1` の全 11 項目 PASS を確認。フロント配信(CSP ヘッダー・WASM ブート・LIFF SDK ロード)も確認。その後 `cdk destroy` で全リソースを削除し、残骸(CDK 内蔵 Lambda の空 LogGroup 含む)まで手動削除済み。**AWS 上にスタックは存在しない**。
>
> **再検証（2026-08-30、AmazonLambdaExtension 変換後）**: §1 #14 の実装形態変更後に再デプロイし、全 11 項目 PASS を再確認 → 全リソース削除・残骸ゼロ確認済み。

### 検証手順

1. §13 の 1〜5 を dev 環境で実施(実機なし)。`test-acceptance.ps1` の全項目 PASS を確認
2. (任意)既存チャネルで M1〜M4 の実機確認
3. **検証完了後、即座に削除**(下記)

### 削除(課金停止)

```powershell
cd Template.IaC
npx --yes aws-cdk@latest destroy -c env=dev
cd ..
# 残骸確認(いずれも 0 件になること)
aws cloudformation describe-stacks --stack-name template-aws-line-miniapp-dev  # 存在しないエラーが正
aws s3api list-buckets --query "Buckets[?contains(Name,'template-aws-line-miniapp')].Name"
aws dynamodb list-tables --query "TableNames[?contains(@,'template-aws-line-miniapp')]"
aws secretsmanager list-secrets --include-planned-deletion --query "SecretList[?contains(Name,'template-aws-line-miniapp')].Name"
aws logs describe-log-groups --query "logGroups[?contains(logGroupName,'template-aws-line-miniapp')].logGroupName"
```

- dev は全リソース DESTROY 指定のため原則残骸なし。テンプレートと同じく `CustomS3AutoDeleteObjects` の LogGroup だけ残ることがある(空・無課金だが手動削除する)。
- Secret は CloudFormation 削除で即時削除される。`--include-planned-deletion` で確認し、猶予期間付きで残っていた場合は `aws secretsmanager delete-secret --force-delete-without-recovery` を実行。
- 検証期間中の概算コスト: **$0.01 未満**(CloudFront/Lambda/DynamoDB は無料枠内。Secrets Manager $0.40/月の日割り + HTTP API 数百リクエスト分)。

---

## 16. ランニングコスト(参考: デプロイしたまま置いた場合)

| サービス | デモ運用(数ユーザー) | 10万 MAU(v2 §10.1 相当) |
|---|---|---|
| DynamoDB | 〜$0.01 | 〜$3 |
| Lambda | $0(無料枠) | 〜$2 |
| CloudFront + S3 | $0〜0.01 | $5〜10 |
| API Gateway (HTTP API) | 〜$0.01 | 〜$4(300万req × $1.29/100万) |
| Secrets Manager | $0.40 | $0.40 |
| **合計** | **約 $0.4/月** | **約 $15〜20/月** |

v2 の Function URL 案との差は HTTP API のリクエスト課金(+〜$4/月 @10万MAU)。サンプルの検証用途では誤差。本番でこの差が問題になる規模なら、v2 どおり Function URL + OAC へ置き換える(その場合はルーティングを単一 Lambda 内へ移す設計変更を伴う。§1 変更点#1 の逆適用)。

---

## 17. 初日に確定すべき不可逆判断(v2 §13 継承)

| # | 項目 | 本書での扱い |
|---|---|---|
| 1 | プロバイダー構成 | 手動・検証は既存チャネル再利用(§11)。本番構築時に要確定 |
| 2 | 内部ユーザー ID の間接参照 | 採用(Guid v7) |
| 3 | `version` 楽観ロック | 初日から実装 |
| 4 | `ValidAlgorithms` / `aud` 検証 | 実装 + 自動テスト #3〜6 で担保 |
| 5 | フロント技術 | Blazor WASM(案C)で確定 |

---

## 18. 前提の確認事項(ユーザー判断が必要な点)

実装は以下の前提で進める。変更があれば指示すること。

1. **配置場所と名称**: `D:\GitHubTemplate\template-aws-line-miniapp\`、スタック名 `template-aws-line-miniapp-{env}`
2. **検証用 LINE チャネル**: `_Work-Host-Line` の既存チャネルを再利用(実機検証を行う場合)。実機検証なし(自動テストのみ)でも主要機能は検証可能
3. **WAF 見送り**(§1 #3)とステージスロットリングによる代替
4. **testJwks 機構の採用**(dev 限定、prod へは構造的に持ち込み不可)— 不要なら削除可能だが、実機なしでの AWS 検証手段がなくなる
5. **自前 JWT 有効期限 7 日**(v2 既定。利用実態での調整方針も v2 §3.5 を継承)
