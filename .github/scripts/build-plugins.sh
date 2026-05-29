#!/usr/bin/env bash
#
# Build every plugin csproj under probes/, checkers/, notifications/,
# publish each as a self-contained zip into ./dist/, and regenerate
# plugin-manifest.json at the repo root.
#
# Inputs (env):
#   BUILD_NUMBER  CI run number; appended as `-ci.<n>` suffix to plugin version
#   GIT_SHA       commit being built (recorded in manifest metadata)
#   REPO_URL      https URL of the plugins repo (used to build release downloadUrl)
#
set -euo pipefail

BUILD_NUMBER="${BUILD_NUMBER:-0}"
GIT_SHA="${GIT_SHA:-unknown}"
REPO_URL="${REPO_URL:-https://github.com/remygrandin/Mone-Plugins}"

ROOT="$(pwd)"
DIST_DIR="$ROOT/dist"
WORK_DIR="$DIST_DIR/.work"
MANIFEST="$ROOT/plugin-manifest.json"

rm -rf "$DIST_DIR"
mkdir -p "$DIST_DIR" "$WORK_DIR"

entries_file="$(mktemp)"
trap 'rm -f "$entries_file"' EXIT

# Build & package one plugin csproj.
# Args: <csproj-path> <pluginType-string>
package_plugin() {
  local csproj="$1"
  local plugin_type="$2"

  local proj_dir
  proj_dir="$(dirname "$csproj")"
  local csproj_name
  csproj_name="$(basename "$csproj" .csproj)"

  # Locate the primary plugin source (any *.cs declaring a Plugin class)
  local src
  src="$(grep -lE 'public[[:space:]]+(sealed[[:space:]]+)?class[[:space:]]+\w+Plugin' "$proj_dir"/*.cs | head -n1 || true)"
  if [[ -z "$src" ]]; then
    echo "::error::No plugin class found under $proj_dir"
    return 1
  fi

  # Extract Name => "..."
  local display_name
  display_name="$(grep -oE 'Name[[:space:]]*=>[[:space:]]*"[^"]+"' "$src" \
    | sed -E 's/.*"([^"]+)".*/\1/' | head -n1)"
  display_name="${display_name:-$csproj_name}"

  # Extract Description => "..."
  local description
  description="$(grep -oE 'Description[[:space:]]*=>[[:space:]]*"[^"]+"' "$src" \
    | sed -E 's/.*"([^"]+)".*/\1/' | head -n1 || true)"

  # Extract Version => new(X, Y, Z)
  local base_version
  base_version="$(grep -oE 'Version[[:space:]]*=>[[:space:]]*new[[:space:]]*(Version[[:space:]]*)?\([[:space:]]*[0-9]+[[:space:]]*,[[:space:]]*[0-9]+[[:space:]]*,[[:space:]]*[0-9]+[[:space:]]*\)' "$src" \
    | grep -oE '[0-9]+[[:space:]]*,[[:space:]]*[0-9]+[[:space:]]*,[[:space:]]*[0-9]+' \
    | tr -d ' ' | tr ',' '.' | head -n1)"
  base_version="${base_version:-1.0.0}"

  local version="${base_version}-ci.${BUILD_NUMBER}"

  echo "::group::Packaging $display_name ($plugin_type) v$version"

  local publish_dir="$WORK_DIR/$display_name"
  rm -rf "$publish_dir"
  dotnet publish "$csproj" \
    --configuration Release \
    --no-restore \
    --nologo \
    --no-self-contained \
    --output "$publish_dir"

  # Drop framework reference assemblies that may leak into publish output;
  # plugins are loaded into the host's shared runtime.
  rm -f "$publish_dir/Mone.Contracts.dll" "$publish_dir/Mone.Contracts.pdb" 2>/dev/null || true

  local zip_name="${display_name}-${version}.zip"
  local zip_path="$DIST_DIR/$zip_name"
  (cd "$publish_dir" && zip -qr "$zip_path" .)

  local sha
  sha="$(sha256sum "$zip_path" | awk '{print $1}')"
  local size
  size="$(stat -c%s "$zip_path")"
  local download_url="${REPO_URL}/releases/download/latest/${zip_name}"

  jq -n \
    --arg name "$display_name" \
    --arg version "$version" \
    --arg description "${description:-}" \
    --arg pluginType "$plugin_type" \
    --arg downloadUrl "$download_url" \
    --arg sha256 "$sha" \
    --argjson fileSize "$size" \
    --arg sourceProject "${csproj#./}" \
    '{
      name: $name,
      version: $version,
      description: (if $description == "" then null else $description end),
      pluginType: $pluginType,
      downloadUrl: $downloadUrl,
      sha256: $sha256,
      fileSize: $fileSize,
      sourceProject: $sourceProject
    }' >> "$entries_file"

  echo "  -> $zip_name ($size bytes, sha256=$sha)"
  echo "::endgroup::"
}

shopt -s nullglob
for csproj in probes/*/*.csproj; do
  package_plugin "$csproj" "Probe"
done
for csproj in checkers/*/*.csproj; do
  package_plugin "$csproj" "Checker"
done
for csproj in notifications/*/*.csproj; do
  package_plugin "$csproj" "AlertChannel"
done

# Compose manifest. We deliberately write `null` fields so consumers see the full
# schema; PluginRepositoryService treats omitted/null fields as optional.
jq -s \
  --arg generatedAt "$(date -u +'%Y-%m-%dT%H:%M:%SZ')" \
  --arg commit "$GIT_SHA" \
  --argjson buildNumber "$BUILD_NUMBER" \
  --arg repository "$REPO_URL" \
  '{
    generatedAt: $generatedAt,
    commit: $commit,
    buildNumber: $buildNumber,
    repository: $repository,
    plugins: (. | sort_by(.pluginType, .name))
  }' "$entries_file" > "$MANIFEST"

rm -rf "$WORK_DIR"

echo "Wrote $(jq '.plugins | length' "$MANIFEST") plugins to plugin-manifest.json"
