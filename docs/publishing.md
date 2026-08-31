# Publishing a sanitized standalone repository

The working tree can be prepared for public use, but the development
repository's existing Git history must not be published. Earlier commits and
refs contain personal author information, environment-specific Azure inventory,
and internal development-session identifiers.

## Required publication shape

Create a new repository from the sanitized working-tree snapshot:

- No old `.git` directory.
- No copied branches, tags, stashes, reflogs, notes, or remotes.
- One fresh root commit.
- A public GitHub noreply author identity.
- No internal automation or session trailers in the commit message.

Do not use a mirror push, `git push --all`, `git push --mirror`, history rewrite
followed by force-push, or a new remote attached to the development repository.
A new root is simpler to inspect and harder to misuse.

## Release checklist

### Source and configuration

- [ ] `scripts/configure-existing-azure.sh` contains placeholders only.
- [ ] No Azure subscription, tenant, resource-group, account, project, Toolbox,
      deployment, or personal host names are present.
- [ ] No credentials, keys, tokens, passwords, certificates, or connection
      secrets are present.
- [ ] Local configuration, databases, manifests, and build output are ignored.
- [ ] Sensitive AI telemetry remains false by default.
- [ ] Managed mode still requires explicit resource-change acknowledgement.

### Assets and documentation

- [ ] PDF, image, and office-document metadata contains no personal or
      organization-sensitive values.
- [ ] Example documents are licensed for redistribution.
- [ ] `THIRD-PARTY-NOTICES.md` and `licenses/` cover every redistributed
      browser library and template asset.
- [ ] README setup commands use placeholders.
- [ ] Security and privacy documentation matches executable behavior.
- [ ] All SVG files parse as XML and render legibly in light and dark themes.
- [ ] `LICENSE` contains the intended public license.

### Validation

- [ ] Clean restore, build, and test succeed.
- [ ] The default AppHost manifest contains connection resources but no Azure
      provisioning or Toolbox reconciliation resource.
- [ ] JSON, YAML, shell, Markdown links, and SVG syntax validate.
- [ ] The dev container builds in a clean environment.
- [ ] A high-confidence secret scanner reports no findings.
- [ ] A final manual search for names, emails, UUIDs, home paths, Azure host
      names, and internal identifiers is clean.

### Repository settings

- [ ] Secret scanning and push protection are enabled.
- [ ] Dependabot alerts and security updates are enabled.
- [ ] Private vulnerability reporting is enabled.
- [ ] Branch protection or repository rules are configured for `main`.
- [ ] Codespaces forwarded ports remain private.

## Create the snapshot

1. Finish and validate the sanitized source tree.
2. Create an empty directory outside the development repository.
3. Copy the working-tree contents only. Exclude `.git`, `bin`, `obj`, local
   databases, user-secrets stores, generated manifests, logs, and test output.
4. Inspect the new directory before initializing Git.
5. Initialize a new repository:

   ```bash
   git init --initial-branch=main
   git config user.name "<public-github-name>"
   git config user.email "<github-noreply-address>"
   chmod +x scripts/configure-existing-azure.sh
   git add .
   git diff --cached --check
   git commit -m "Initial public release"
   ```

6. Inspect exactly what the root commit contains:

   ```bash
   git status --short
   git ls-tree -r --name-only HEAD
   git log --format=fuller --decorate --all
   git show --stat --oneline HEAD
   ```

7. Create an empty GitHub repository without generated README, license, or
   `.gitignore` files.
8. Add that new remote only inside the new snapshot repository.
9. Push only `main`.

Keep the original development repository local and unconnected to the public
remote.

## Suggested scans

Run an established secret scanner locally against the new root and its single
commit. Also perform targeted manual searches because non-secret cloud inventory
usually is not detected by secret scanners.

Review at least:

```text
subscription
tenant
resourceGroup
clientSecret
apiKey
Authorization:
@company-domain
/home/
C:\Users\
services.ai.azure.com
openai.azure.com
session-specific commit trailers
```

Some terms legitimately appear in documentation and source identifiers. Inspect
each match rather than suppressing broad categories.

## Why a fresh root matters

Deleting a value from the latest file does not remove it from old commits.
Removing a branch name locally does not guarantee the associated objects,
reflogs, tags, or remote refs will not be pushed later. A standalone snapshot
contains only the reviewed state and makes the public provenance explicit.
