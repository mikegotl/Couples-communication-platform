#!/usr/bin/env bash
# Fails if a tracked file looks like it contains a credential. Cheap, dependency-free, and run in
# CI on every push; GitHub secret scanning and push protection are the second line.
set -euo pipefail
cd "$(git rev-parse --show-toplevel)"

patterns=(
  'sk-[A-Za-z0-9_-]{20,}'                          # OpenAI/Anthropic-style API keys
  'AKIA[0-9A-Z]{16}'                               # AWS access key id
  'gh[pousr]_[A-Za-z0-9]{36,}'                     # GitHub tokens
  'xox[abprs]-[A-Za-z0-9-]{10,}'                   # Slack tokens
  '-----BEGIN ([A-Z]+ )?PRIVATE KEY-----'          # private keys
  'AccountKey=[A-Za-z0-9+/=]{20,}'                 # Azure storage
  'SG\.[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]{16,}'     # SendGrid
  '(?i)password=(?!couples_local_dev_only|none|postgres\b)[^;"'"'"'\s]{8,}'  # connection-string passwords other than the local dev ones
)

status=0
for pattern in "${patterns[@]}"; do
  if matches=$(git ls-files -z -- ':!*.png' ':!*.jpg' ':!*.ico' ':!eng/scan-secrets.sh' | xargs -0 grep -nIP -e "$pattern" 2>/dev/null); then
    echo "Possible secret matching /$pattern/:"
    echo "$matches" | sed 's/^/  /'
    status=1
  fi
done

if [ "$status" -eq 0 ]; then
  echo "No secrets found in tracked files."
fi
exit "$status"
