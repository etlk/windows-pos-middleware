# Releasing Cloud POS Middleware

## Branch model

| Branch | Purpose |
|---|---|
| `main` | Day-to-day development. Every change lands here first. |
| `production` | Distribution branch. **Every push to it publishes a release.** |

## How to cut a release

```bash
git checkout main && git pull
# make sure main is green (dotnet test MiddlewareApp.Core.Tests)

git checkout production && git pull
git merge --ff-only main
git push origin production
```

That push triggers `.github/workflows/release.yml`, which:

1. **Auto-bumps the version** from the latest `v*` tag (see rules below)
2. Runs the Core unit tests (a failure aborts the release)
3. Publishes the self-contained win-x64 build, stamped with the new version
4. Builds the Inno Setup installer and a portable zip
5. Tags the commit `vX.Y.Z` and creates a GitHub Release with both files attached

The release appears at
<https://github.com/etlk/windows-pos-middleware/releases> a few minutes later.
Customers install/upgrade by running `CloudPOS-Middleware-Setup-X.Y.Z.exe`
(settings live in `%APPDATA%\CloudPOSMiddleware` and survive upgrades).

## Version bumping rules

The workflow looks at the commit messages between the last tag and HEAD:

| Marker in any commit message | Bump | Example |
|---|---|---|
| *(none)* | **patch** | 1.2.3 → 1.2.4 |
| `#minor` | **minor** | 1.2.3 → 1.3.0 |
| `#major` | **major** | 1.2.3 → 2.0.0 |

So for a normal fix, just merge — you get a patch release. For a feature
release, include `#minor` in a commit message on `main` (e.g.
`git commit -m "Add 58mm paper support #minor"`). If no tag exists yet, the
first release is `v1.0.0`.

## Re-running / manual release

The workflow can also be started by hand from the GitHub **Actions** tab
("Release" → *Run workflow*) — useful if a run failed for an environment
reason. It is safe to re-run: the same commit produces the same version tag
and updates the existing release's assets.

## What is NOT automated (yet)

- **Code signing** — the installer is unsigned, so first-time users see a
  SmartScreen warning ("More info → Run anyway"). Before wide distribution,
  add signing credentials (Azure Trusted Signing or an OV certificate) and a
  signing step in the workflow.
- **Auto-update inside the app** — customers upgrade by running the newer
  installer. If update friction grows, consider Velopack.

## Local one-off build (no CI)

```bash
dotnet publish MiddlewareApp.csproj -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -o publish/win-x64
# Windows only, with Inno Setup installed:
iscc /DAppVersion=1.2.3 installer/CloudPOSMiddleware.iss
```
