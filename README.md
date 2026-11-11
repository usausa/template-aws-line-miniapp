# Template project for LINE Mini App on AWS

[Template](https://usausa.github.io/template/) project for LINE Mini App on AWS.

# ✅ Implement

| Category | Feature |
| --- | --- |
| Auth | LINE Login (LIFF) / Two-token scheme (LINE ID token to own JWT, ES256) / JWT validation in Lambda |
| Data | Per-user JSON on DynamoDB / Optimistic locking (version) / Account deletion (one transaction) |
| API | API Gateway HTTP API / Lambda (AmazonLambdaExtension) |
| Hosting | S3 + CloudFront (OAC, security headers, CSP) |
| Infra | AWS CDK (C#) / Secrets Manager (JWT signing key) / Setup and removal scripts (PowerShell) |
| Test | Acceptance tests on AWS without a LINE device (test JWKS) |

# 📚 Docs

The documents in `docs` are in Japanese.

| Document | Contents |
| --- | --- |
| [SPEC.md](SPEC.md) | Specification |
| [architecture.md](docs/architecture.md) | Design (principles, AWS resources, authentication, data model, security) |
| [setup.md](docs/setup.md) | New project, setup, acceptance tests, removal, program structure, and local development |
| [notes.md](docs/notes.md) | Running costs and known limitations |

# 🔗 Related Projects

| Project | Description |
| --- | --- |
| [template-aws-lambda](https://github.com/usausa/template-aws-lambda) | AWS Lambda + API Gateway template |
| [template-aws-api-tenant](https://github.com/usausa/template-aws-api-tenant) | Multi-tenant serverless API template on AWS |
| [template-aws-s3-wasm](https://github.com/usausa/template-aws-s3-wasm) | Blazor WebAssembly template on S3 + CloudFront with Cognito |
| [template-azure-functions](https://github.com/usausa/template-azure-functions) | Azure Functions (isolated worker) template |

# 📦 My Libraries

| Library | Description |
| --- | --- |
| [AmazonLambdaExtension](https://github.com/usausa/amazon-lambda-extension) | Source generator based Lambda handler |
| [Usa.Smart.Mapper](https://github.com/usausa/Smart-Net-Mapper) | Object mapper |
