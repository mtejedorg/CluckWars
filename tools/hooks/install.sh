#!/bin/sh
# Installs the tracked build-number hook into this clone as pre-commit and pre-merge-commit
# (graphify's hooks are post-commit/post-checkout, no clash). The hook only acts on the develop branch.
set -e
root=$(git rev-parse --show-toplevel)
for h in pre-commit pre-merge-commit; do
  cp "$root/tools/hooks/pre-commit" "$root/.git/hooks/$h"
  chmod +x "$root/.git/hooks/$h"
done
echo "build-number hooks installed (pre-commit, pre-merge-commit; develop only)"
