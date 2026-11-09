# Backup and restore policy

Status: policy for staging and pilot. Nothing is deployed yet; the hosting choice (an open owner
decision) must meet these requirements.

## Requirements for the host
- Managed PostgreSQL with encrypted storage and encrypted backups (provider-managed keys at
  minimum; customer-managed keys preferred).
- Daily automated backups plus point-in-time recovery (write-ahead log) for **14 days**. No
  longer: every day a backup is kept is a day deleted data still exists somewhere.
- Backups stay in the same jurisdiction as the primary database and are never copied to
  developer machines or other environments. Staging never uses production data.
- Access to backups and restores limited to two named operators, with the provider's audit log on.
- The Data Protection key-ring certificate is stored in the host's secret store, backed up
  separately from the database, and rotated yearly.

## Deletion and backups
Deleting an account erases it from the live database at once (ADR-0007, `DeletionSweepTests`).
Copies inside backups age out within 14 days. If a backup is restored:
1. Before restoring to an earlier point in time, export the completed deletion requests from the
   current database (ids only):
   `\copy (SELECT * FROM couples.data_deletion_requests WHERE "Status" = 'Completed') TO 'deletions.csv' CSV HEADER`
2. Restore.
3. Import any of those rows that are missing (`\copy ... FROM 'deletions.csv'` into a temporary
   table, then insert rows whose `Id` isn't present).
4. Run the worker. It erases every account that has a completed deletion request but exists
   again (`ProcessPendingDeletionsAsync`), and logs a warning for each.

Deletion requests are kept 400 days, longer than any backup, so step 4 alone covers restores of
the same database; steps 1 and 3 cover restoring to a point before recent deletions.

## Testing
- Restore the latest backup into an isolated environment at least quarterly, run the worker once,
  check the app starts and `/health/ready` is healthy, then destroy the environment.
- Record each test (date, backup used, time to restore) in the operations log.
