# Mone-Plugins

First-party plugin collection for [Mone](https://github.com/remygrandin/Mone).
Each plugin is published as a standalone DLL bundle. Every CI build produces a
new immutable GitHub Release tagged `build-<run_number>` carrying both the
plugin zips and a `mone-plugins.json` asset describing them. Mone's
`PluginRepositoryService` enumerates the most recent releases, fetches each
release's `mone-plugins.json`, and surfaces every version in the dashboard so
users can install or pin any published build.

## Documentation

[`docs/`](docs/README.md) documents every plugin in this repo — a main index plus
one page per plugin describing how it works, its parameters, the metrics it
emits, and how results map to a monitoring status.

## Layout

```
docs/          Per-plugin documentation (start at docs/README.md)
probes/        Active and passive probes (Ping, Https, Webhook, Syslog, SnmpTrap)
checkers/      Threshold/value evaluators run against probe results
notifications/ Alert channels (Email, Slack, Teams, Webhook)
tests/         xUnit test project covering all plugins
```

## Build & release

`.github/workflows/build-plugins.yml` runs on every push to `main`:

1. Restores the solution and runs `dotnet test`.
2. Calls `.github/scripts/build-plugins.sh`, which `dotnet publish`-es every
   plugin csproj, zips each output, and writes `dist/mone-plugins.json`.
3. Publishes a new immutable GitHub Release tagged `build-<run_number>` with
   all zips plus `mone-plugins.json` as assets, and points "Latest release"
   at it.

Each plugin's runtime `Version` is read from its source class and suffixed with
the CI run number (e.g. `1.0.0-ci.42`), so every successful build is uniquely
addressable. The release tag `build-<n>` is what Mone's sync uses to keep
versions distinct across builds.

## Manifest schema (`mone-plugins.json`)

Each release uploads a `mone-plugins.json` asset consumed by
`Mone.Infrastructure.Services.PluginRepositoryService`. Shape:

```json
{
  "schemaVersion": 1,
  "generatedAt": "2026-06-03T12:34:56Z",
  "releaseTag": "build-42",
  "commit": "<sha>",
  "plugins": [ { ...entry... } ]
}
```

Each plugin entry must include:

| Field         | Required | Notes                                                          |
|---------------|----------|----------------------------------------------------------------|
| `name`        | yes      | Friendly plugin name (matches the `Name` property in source)   |
| `version`     | yes      | Semver string, e.g. `1.0.0-ci.42`                              |
| `pluginType`  | yes      | `Probe`, `Checker`, or `AlertChannel`                          |
| `downloadUrl` | yes      | Public HTTPS URL to a ZIP archive on this release              |
| `sha256`      | yes      | Lowercase hex digest of the ZIP                                |
| `description` | no       |                                                                |
| `fileSize`    | no       | Bytes                                                          |

## Local build

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet restore Mone-Plugins.slnx
dotnet test Mone-Plugins.slnx --configuration Release --no-restore

# Dry-run the packaging script (writes dist/*.zip + dist/mone-plugins.json):
BUILD_NUMBER=0 GIT_SHA=local REPO_URL=https://github.com/remygrandin/Mone-Plugins \
  bash .github/scripts/build-plugins.sh
```

The script needs `jq`, `zip`, and `sha256sum` on `PATH`. The plugin csprojs
reference `Mone.Contracts` via a sibling-path `ProjectReference`, so the
`Mone` repo must be checked out next to `Mone-Plugins/` for builds to resolve.
