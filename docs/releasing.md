# Releasing dotdbg

The [release workflow](../.github/workflows/release.yml) publishes the `DotDbg` .NET tool when a `vMAJOR.MINOR.PATCH` tag (or a prerelease tag such as `v1.1.0-rc.1`) is pushed. It accepts tags pointing to commits on `main`. The release version comes from the tag and overrides the project's `0.0.0-local` default; do not edit the project to release a version.

The workflow runs unit tests on Windows and the full debugger workload suite on Ubuntu, packs the tool on Ubuntu, exchanges a GitHub OIDC token for a short-lived NuGet API key, publishes the package, and creates a GitHub release with the `.nupkg` attached. Run the full suite locally on Windows before tagging; GitHub-hosted Windows runners intermittently fail to attach to target processes. GitHub generates release notes from merged pull requests. The fresh v0.1.0 history has no pull requests to summarize. NuGet versions cannot be replaced, so use a new tag and version for any correction.

## One-time configuration

1. On nuget.org, sign in as the account that will own `DotDbg`. Under **Trusted Publishing**, add a GitHub policy with owner `akeit0`, repository `dotdbg`, workflow file `release.yml`, and environment `release`. Scope the policy to package ID `DotDbg` for new packages and new versions.
2. Create a GitHub Actions environment named `release`. In its environment variables, set `NUGET_USER` to the nuget.org account's **profile name**, not its email address. The `publish` job reads this variable from the environment. `NuGet/login` exchanges the job's OIDC token for a temporary `NUGET_API_KEY`; do not store an API key as a secret.
3. Check the package ID on nuget.org before the first tag. The workflow cannot claim an ID already owned by another account.

NuGet policies for private GitHub repositories can start in a seven-day temporary activation window. If it expires before the first publish, reactivate the policy in nuget.org. A successful publish completes activation. See [NuGet's trusted publishing guide](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).

The GitHub repository and NuGet package are public. Keep private evaluation projects and local package caches out of the published source snapshot.

## Release a version

Review the changes and confirm the cross-platform checks pass. They run for pull requests to `main`; for a direct commit, start **Cross-platform checks** manually on `main`. Create an annotated tag at the commit to release and push that tag:

```shell
git switch main
git pull --ff-only
git tag -a v0.1.0 -m "dotdbg v0.1.0"
git push origin v0.1.0
```

Pushing the tag starts publication. Inspect the Actions run, then review the generated notes on the GitHub release and the package page on nuget.org. Do not reuse a published version tag.

For a local packaging check before tagging:

```shell
dotnet pack src/DotDbg/DotDbg.csproj -c Release -p:Version=0.1.0 -o artifacts/packages
dotnet tool install DotDbg --tool-path artifacts/tools --add-source artifacts/packages --version 0.1.0
./artifacts/tools/dotdbg --help
```

Use `./artifacts/tools/dotdbg.exe --help` for the final command on Windows.
