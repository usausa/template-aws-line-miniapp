# 注意点

## 💰 ランニングコスト

| サービス | デモ運用（数ユーザー） | 10万 MAU |
|---|---|---|
| DynamoDB | 〜$0.01 | 〜$3 |
| Lambda | $0（無料枠） | 〜$2 |
| CloudFront + S3 | $0〜0.01 | $5〜10 |
| API Gateway (HTTP API) | 〜$0.01 | 〜$4 |
| Secrets Manager | $0.40 | $0.40 |
| **合計** | **約 $0.4/月** | **約 $15〜20/月** |

Secrets Manager の $0.40/月以外はほぼ無料枠。本番で API Gateway のリクエスト課金が問題になる規模なら、v2 仕様どおり Lambda Function URL + OAC へ置き換える（ルーティングを単一 Lambda に寄せる設計変更を伴う）。

## 📋 既知の制約

| 項目 | 内容 |
|---|---|
| Lambda コールドスタート | 管理ランタイムのため初回に JIT ウォームアップ（実測 700ms 程度）。詰めるなら Native AOT だが Windows→Linux ビルドに Docker が要るため不採用 |
| CSP のホスト網羅 | LIFF SDK の通信先が非公開。実機初回テストで violation を確認し追記する（[SPEC.md](../SPEC.md) §10.1） |
| フロント初回ダウンロード | WASM + AOT で数 MB。ミニアプリは LINE 内でキャッシュが効きやすい前提で許容（[SPEC.md](../SPEC.md) §1 #6） |
| testJwks | dev 専用。prod では EnvironmentConfig が synth 時に例外を投げ、構造的に持ち込めない |
| 現在のデプロイ状態 | 一度デプロイ・検証して**削除済み**。AWS 上にスタックは無い |
| CI/CD | 未同梱 |
