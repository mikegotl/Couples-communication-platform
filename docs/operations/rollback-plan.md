# Release and rollback plan

## Principles

- **Images are immutable and versioned** (`bridgetogether-web:<version>`); never redeploy a tag
  with different contents. Rolling back means running the previous image.
- **Migrations are forward-only and backward-compatible for one release** (expand, then
  contract). A release may add tables, nullable columns or indexes. It may not drop or rename
  anything the previous release still reads. Removal happens in a later release, once nothing
  reads it. This is what makes an image rollback safe without touching the database.
- **Feature flags are the first kill switch** (`docs/operations/incident-response.md`). Prefer
  turning a feature off to rolling back a release.
- Never use `dotnet ef database update <earlier>` against staging or production: down migrations
  drop data. If the schema itself is wrong, fix forward with a new migration.

## Releasing (staging)

1. CI is green on `main` (build, tests, security checks, image builds, Terraform validation).
2. Build and push both images with the same version tag.
3. `terraform plan` with the new `web_image`/`worker_image`; check only the images change.
4. Apply. The web app migrates on start-up (one replica), then serves. Watch `/health/ready`,
   the error rate in Log Analytics and the `BridgeTogether` metrics for 15 minutes.
5. Record the version, time and the previous version in the release log.

## Rolling back

1. Set the previous image tags and apply (or in an emergency, `az containerapp update --image`
   for each app). The previous image runs against the expanded schema, which it tolerates by
   the rule above.
2. Leave the database as it is. `__ef_migrations_history` will list a migration the old image
   doesn't know; EF ignores that when `Database__MigrateOnStartup` finds nothing to apply.
3. If data was damaged, not just code: point-in-time restore (14-day window) into a **new**
   server, compare, then switch the connection string. Follow the deletion re-erasure steps in
   `docs/operations/backup-policy.md` so restored copies of deleted accounts are erased again.

## Key-ring certificate rotation

Key Vault renews the certificate before it expires. Restart the web app afterwards. Existing
sign-in cookies and in-progress forms stop working once (people sign in again); nothing else
depends on the key ring. To avoid that sign-out, mount the previous certificate as well and
pass both to `UnprotectKeysWithAnyCertificate` (not implemented; not needed for the pilot).
