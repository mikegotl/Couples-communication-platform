#!/usr/bin/env bash
# Creates throwaway certificates for the local staging rehearsal: a TLS certificate for
# PostgreSQL and a PKCS#12 certificate that encrypts the Data Protection key ring. Never use
# these anywhere real; real ones come from Key Vault (deploy/azure).
set -euo pipefail
cd "$(dirname "$0")"
mkdir -p certs
openssl req -x509 -newkey rsa:2048 -nodes -days 30 -subj "/CN=postgres" \
  -keyout certs/postgres.key -out certs/postgres.crt 2>/dev/null
openssl req -x509 -newkey rsa:2048 -nodes -days 30 -subj "/CN=bridgetogether-keyring" \
  -keyout certs/keyring.key -out certs/keyring.crt 2>/dev/null
openssl pkcs12 -export -inkey certs/keyring.key -in certs/keyring.crt -out certs/keyring.pfx -passout pass:staging-rehearsal-only
chmod 600 certs/postgres.key
# The postgres image runs as uid 70 (alpine); the app images run as uid 1654.
if [ "$(id -u)" = "0" ]; then chown 70:70 certs/postgres.key certs/postgres.crt; chown 1654:1654 certs/keyring.pfx; fi
echo "Certificates written to deploy/staging/certs (git-ignored)."
