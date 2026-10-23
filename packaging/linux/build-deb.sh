#!/usr/bin/env bash
# Builds the .deb from a self-contained linux-x64 publish folder.
# Usage: packaging/linux/build-deb.sh <version> <publish-dir> <output-dir>
# Requires nfpm on PATH. Run from the repository root.
set -euo pipefail

version="$1"
publish_dir="$(realpath "$2")"
out_dir="$3"
mkdir -p "$out_dir"

chmod +x "$publish_dir/Lumen"
# nfpm only expands environment variables in some fields, so fill in the template ourselves.
config="$(mktemp --suffix=.yaml)"
# Debian sorts "~" before release versions, so 0.1.0~ci.42 < 0.1.0.
deb_version="${version/-/\~}"
sed -e "s|@VERSION@|$deb_version|g" -e "s|@PUBLISH_DIR@|$publish_dir|g" packaging/linux/nfpm.yaml > "$config"

nfpm package --config "$config" --packager deb \
  --target "$out_dir/Lumen-$version-linux-amd64.deb"
