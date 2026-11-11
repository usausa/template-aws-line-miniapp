# Template project for LINE Mini App on AWS

[English](README.md) | 日本語

[Template](https://usausa.github.io/template/) project for LINE Mini App on AWS.

# ✅ 実装

| 分類 | 機能 |
| --- | --- |
| 認証 | LINE ログイン(LIFF) / 2 トークン方式(LINE ID トークンから自前 JWT へ・ES256) / Lambda 内での JWT 検証 |
| データ | DynamoDB のユーザーごとの JSON / 楽観ロック(version) / 退会(1 トランザクション) |
| API | API Gateway HTTP API / Lambda(AmazonLambdaExtension) |
| 配信 | S3 + CloudFront(OAC・セキュリティヘッダー・CSP) |
| インフラ | AWS CDK(C#) / Secrets Manager(JWT 署名鍵) / 構築・削除スクリプト(PowerShell) |
| テスト | LINE 実機なしの AWS 上での受け入れテスト(テスト用 JWKS) |

# 📚 ドキュメント

| ドキュメント | 内容 |
| --- | --- |
| [SPEC.md](SPEC.md) | 仕様 |
| [architecture.md](docs/architecture.md) | 設計(中心思想・AWS リソース・認証・データモデル・セキュリティ) |
| [setup.md](docs/setup.md) | 新規プロジェクト・セットアップ・受け入れテスト・削除・プログラム構成・ローカル開発 |
| [notes.md](docs/notes.md) | ランニングコスト・既知の制約 |

# 🔗 関連プロジェクト

| プロジェクト | 説明 |
| --- | --- |
| [template-aws-lambda](https://github.com/usausa/template-aws-lambda) | AWS Lambda + API Gateway のテンプレート |
| [template-aws-api-tenant](https://github.com/usausa/template-aws-api-tenant) | AWS マルチテナントサーバーレス API のテンプレート |
| [template-aws-s3-wasm](https://github.com/usausa/template-aws-s3-wasm) | S3 + CloudFront と Cognito で動かす Blazor WebAssembly のテンプレート |
| [template-azure-functions](https://github.com/usausa/template-azure-functions) | Azure Functions(isolated worker)のテンプレート |

# 📦 自作ライブラリ

| ライブラリ | 説明 |
| --- | --- |
| [AmazonLambdaExtension](https://github.com/usausa/amazon-lambda-extension) | Lambda ハンドラー |
| [Usa.Smart.Mapper](https://github.com/usausa/Smart-Net-Mapper) | オブジェクトマッパー |
