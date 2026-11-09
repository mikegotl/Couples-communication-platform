# Azure staging (UK South): Terraform

Staging infrastructure for the private pilot, as code. **It has been validated
(`terraform validate` with azurerm 5.13) but never applied.** Applying creates resources that
cost money and, with `public_ingress_enabled = true`, a public web address. Both need the owner's
go-ahead.

## What it creates

| Piece | Resource | Settings that matter |
|---|---|---|
| Network | Virtual network `10.40.0.0/16`, subnets for apps, database and vault | Database and vault reachable only inside it |
| Web app | Container App, 1 replica, port 8080 | HTTPS-only ingress (internal unless `public_ingress_enabled`), health probes, forwarded headers trusted from the apps subnet only |
| Worker | Container App, 1 replica, no ingress | Deletion workflow and retention |
| Database | PostgreSQL 17 Flexible Server, Burstable B1ms | Private access only, TLS 1.2+ required (`VerifyFull` from the app), encrypted at rest by the platform, **14-day point-in-time restore**, geo-redundant backup to UK West, statement logging off, `prevent_destroy` |
| Secrets | Key Vault (RBAC, soft delete 90 days, purge protection, private endpoint) | Connection string, pilot code hash, and a Key Vault-generated certificate that encrypts the Data Protection key ring |
| Images | Container Registry (Basic, no admin user, no anonymous pull) | Pulled with a managed identity |
| Logs | Log Analytics, 30-day retention, 1 GB/day cap | The app scrubs logs and traces before they leave (ADR-0014) |

No secret is a Terraform variable except the pilot code **hash**. The database password is
generated, stored in Key Vault, and read by the apps through their managed identity. Terraform
state therefore contains secrets: keep it in a private, encrypted storage account.

## Owner checklist before `terraform apply`

1. Approve the spend (Container Apps consumption, PostgreSQL B1ms with 32 GB and geo-redundant
   backup, Key Vault, a Basic registry, Log Analytics).
2. Choose the transactional email provider. Without one, the deployed app cannot send account
   confirmation emails, so nobody can register. (Open decision in DECISIONS.md.)
3. Complete the clinical and legal reviews listed in `docs/operations/pilot-readiness.md`.
4. Decide whether to expose the app publicly (`public_ingress_enabled`), and on a custom domain.

## Running it (once approved)

```bash
cd deploy/azure
cp staging.tfvars.example staging.tfvars          # git-ignored; fill in
cat > backend.hcl <<'HCL'                           # git-ignored
resource_group_name  = "rg-bridgetogether-tfstate"
storage_account_name = "<state account>"
container_name       = "tfstate"
key                  = "staging.tfstate"
use_azuread_auth     = true
HCL
terraform init -backend-config=backend.hcl
terraform plan -var-file=staging.tfvars -out=staging.plan    # review every resource
terraform apply staging.plan

# Push the images CI built (deploy/docker), then let the apps pull them.
az acr login --name "$(terraform output -raw registry_login_server)"
```

`terraform init` writes `.terraform.lock.hcl`; commit it the first time you run against Azure,
with hashes for every platform you use (`terraform providers lock -platform=linux_amd64
-platform=darwin_arm64`).

## Known limits

- One web replica. Blazor Server keeps each user's session in memory; scaling out needs sticky
  sessions or Azure SignalR Service.
- The web app applies migrations at start-up. With one replica that is safe; see
  `docs/operations/rollback-plan.md` for why migrations are forward-only.
- When Key Vault renews the key-ring certificate (about every 10 months, matching the yearly rotation in `docs/operations/backup-policy.md`), the app needs a restart and
  everyone is signed out once, because old keys were encrypted with the old certificate.
- The forwarded-headers trust is the apps subnet. If the platform's ingress ever reaches the app
  from another range, sign-in stops working (cookies need HTTPS) until `KnownNetworks` is fixed.

## Local rehearsal

`deploy/staging/compose.yaml` runs the same images with production settings, TLS to PostgreSQL,
a certificate-encrypted key ring and a TLS-terminating proxy. See the comments at its top.
