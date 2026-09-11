# NuGet Package Publishing & OIDC Trusted Publishing

> Architecture, setup guide, and operational troubleshooting for automated NuGet package publishing using GitHub Actions and NuGet.org OpenID Connect (OIDC) Trusted Publishing.

---

## 📋 Overview

Centra distributes its 11 modular abstractions, implementations, and distributed provider drivers to [NuGet.org](https://www.nuget.org) using **Trusted Publishing (OIDC)** via [`NuGet/login@v1`](https://github.com/NuGet/login).

Trusted Publishing eliminates long-lived static API keys (`NUGET_API_KEY`) in repository secrets. Instead, the GitHub Actions runner exchanges a short-lived, cryptographically signed OIDC JSON Web Token (JWT) issued by GitHub for a temporary scoped access token from NuGet.org.

---

## ⚠️ Operational Incident Note: Release Event Trigger vs. Workflow Dispatch

### Symptom

During initial release runs, workflow execution failed at the `NuGet Login (Trusted Publisher OIDC)` step with the error:

```text
Error: GitHub OIDC is not available. Ensure your workflow has the required permissions:
  permissions:
    id-token: write
    contents: read
```

### Root Cause Analysis

1. **GitHub Ref Binding on `release` Events**:
   When a workflow triggers on a GitHub release publication (`on: release: types: [published]`), GitHub executes the workflow definition as it existed on the **release tag** (`refs/tags/<tag_name>`), not the latest commit on `main`.
   If a tag was created or pointed to a commit prior to adding `permissions: id-token: write`, the runner launched without OIDC token service environment variables (`ACTIONS_ID_TOKEN_REQUEST_URL` and `ACTIONS_ID_TOKEN_REQUEST_TOKEN`).

2. **NuGet.org Trusted Publisher Policy Mismatch**:
   NuGet.org validates incoming OIDC tokens against the configured Trusted Publisher policy:
   * **Workflow filename constraint**: The token's workflow reference claim (`job_workflow_ref`) must match the exact filename registered in NuGet.org (e.g. `publish-nuget.yml`).
   * **Tag/Branch ref constraints**: Triggering via GitHub release events binds to git tag refs, which can cause validation failures if the Trusted Publisher policy on NuGet.org expects branch refs or if tag creation precedes CI permission commits.

3. **Inflexible Versioning Coupled to Releases**:
   Relying on GitHub release webhooks made it impossible to rebuild, test, or re-pack artifacts with incremental build numbers or prerelease identifiers without generating new git releases.

### Solution: Standard Manual Workflow (`workflow_dispatch`)

The release trigger was replaced with a dedicated, standard manual workflow: [`.github/workflows/publish-nuget.yml`](../../.github/workflows/publish-nuget.yml).

Key changes implemented:

1. **Switched Trigger to `workflow_dispatch`**:
   The workflow runs on-demand against any branch (defaulting to `main`), guaranteeing that the latest workflow definition is loaded.
2. **Explicit OIDC Permissions**:
   Top-level permissions explicitly grant the required scopes:
   ```yaml
   permissions:
     contents: read
     packages: write
     id-token: write
   ```
3. **Automated Run-Based Versioning**:
   The workflow calculates semantic package versions using `${BASE_VER}.${RUN_NUM}` with an optional prerelease suffix:
   ```bash
   FULL_VER="${BASE_VER}.${RUN_NUM}"
   if [ -n "$SUFFIX" ]; then
     FULL_VER="${FULL_VER}-${SUFFIX}"
   fi
   ```
4. **Reliable Artifact Packaging & Publishing**:
   Packages are compiled, packed into `./nupkgs`, authenticated via `NuGet/login@v1`, pushed to `https://api.nuget.org/v3/index.json`, and preserved as workflow run artifacts.

---

## 🔧 NuGet.org Trusted Publisher Setup

To publish packages using this workflow, configure a Trusted Publisher in NuGet.org:

1. Sign in to **[NuGet.org](https://www.nuget.org)**.
2. Navigate to **Account Settings** -> **Trusted Publishers** (or Organization Settings if publishing under an organization).
3. Click **Add a new trusted publisher** -> select **GitHub Actions**.
4. Fill in the required fields:
   * **Publisher Name**: e.g., `Centra GitHub Actions`
   * **Scope**: Choose whether to apply to all packages owned by the account or specific packages (`Centra.*`).
   * **Repository Owner**: `chadahoochie` (or organization name)
   * **Repository Name**: `Centra`
   * **Workflow Filename**: `publish-nuget.yml` *(must match the filename exactly, without directory paths)*
   * **Environment**: *(leave blank unless using GitHub Environments)*
5. Save the trusted publisher policy.

---

## 🚀 How to Publish Packages

### Via GitHub CLI (`gh`)

To trigger a release from the command line:

```bash
# Publish a prerelease (e.g., version 0.1.0.1-prerelease)
gh workflow run publish-nuget.yml --ref main \
  -f version_base="0.1.0" \
  -f prerelease_suffix="prerelease"

# Publish a stable release (e.g., version 1.0.0.12)
gh workflow run publish-nuget.yml --ref main \
  -f version_base="1.0.0"
```

### Via GitHub Web UI

1. Go to **Actions** in the GitHub repository.
2. Select **Publish NuGet Packages** from the workflow list.
3. Click **Run workflow**:
   * **Branch**: `main`
   * **Base version prefix**: e.g., `0.1.0` or `1.0.0`
   * **Prerelease tag suffix**: e.g., `preview`, `beta`, `rc`, or leave blank for stable releases.
4. Click **Run workflow**.

---

## 📦 Published Packages

The workflow builds and publishes all 30+ Centra packages defined in the solution:
* `Centra.Abstractions` (Umbrella metapackage)
* `Centra.*.Abstractions` (11 modular abstractions)
* `Centra.*` (12 core implementations)
* `Centra.Providers.*` (7 distributed provider drivers)
* `Centra.Hosting`, `Centra.ControlPlane`, `Centra.Aspire.Hosting`

See [Package Ecosystem Architecture](../architecture/package-ecosystem.md) for details on package dependencies and layering.
