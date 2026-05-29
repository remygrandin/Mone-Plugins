# Mone-Plugins

First-party plugin collection for [Mone](https://github.com/remygrandin/Mone).
Each plugin is published as a standalone DLL bundle and registered in
`plugin-manifest.json`, which Mone's `PluginRepositoryService` reads to surface
installable plugins in the dashboard.

## Layout

```
probes/        Active and passive probes (Ping, Https, Webhook, Syslog, SnmpTrap)
checkers/      Threshold/value evaluators run against probe results
notifications/ Alert channels (Email, Slack, Teams, Webhook)
tests/         xUnit test project covering all plugins
```

## Build & release

`.github/workflows/build-plugins.yml` runs on every push to `main`:

1. Restores the solution and runs `dotnet test`.
2. Calls `.github/scripts/build-plugins.sh`, which `dotnet publish`-es every
   plugin csproj, zips each output, and writes `plugin-manifest.json` at the
   repo root.
3. Uploads all zips to the GitHub release tagged `latest` (replacing prior
   assets).
4. Commits the regenerated `plugin-manifest.json` back to `main` with
   `[skip ci]` so the next sync sees the new build.

Each plugin's runtime `Version` is read from its source class and suffixed with
the CI run number (e.g. `1.0.0-ci.42`), so every successful build is uniquely
addressable.

## Manifest schema

`plugin-manifest.json` is consumed by `Mone.Infrastructure.Services.PluginRepositoryService`.
Each entry must include:

| Field         | Required | Notes                                                          |
|---------------|----------|----------------------------------------------------------------|
| `name`        | yes      | Friendly plugin name (matches the `Name` property in source)   |
| `version`     | yes      | Semver string, e.g. `1.0.0-ci.42`                              |
| `pluginType`  | yes      | `Probe`, `Checker`, or `AlertChannel`                          |
| `downloadUrl` | yes      | Public HTTPS URL to a ZIP archive                              |
| `sha256`      | yes      | Lowercase hex digest of the ZIP                                |
| `description` | no       |                                                                |
| `fileSize`    | no       | Bytes                                                          |

## Local build

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet restore Mone-Plugins.slnx
dotnet test Mone-Plugins.slnx --configuration Release --no-restore

# Dry-run the packaging script (writes dist/*.zip + plugin-manifest.json):
BUILD_NUMBER=0 GIT_SHA=local REPO_URL=https://github.com/remygrandin/Mone-Plugins \
  bash .github/scripts/build-plugins.sh
```

The script needs `jq`, `zip`, and `sha256sum` on `PATH`. The plugin csprojs
reference `Mone.Contracts` via a sibling-path `ProjectReference`, so the
`Mone` repo must be checked out next to `Mone-Plugins/` for builds to resolve.
