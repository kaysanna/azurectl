# AzureCTL releases

- Author commits as `kaysan <kaysan@users.noreply.gitlab.com>`.
- Inspect the working tree and fetch/review contributor changes before editing or publishing. Never force-push.
- Publish only one downloadable asset: `AzureCTL.exe`. The updater reads its size and SHA-256 digest from GitHub's release API; do not add ZIPs, manifests or checksum files.
- Keep only the newest published release. Verify its uploaded executable and latest-release feed before deleting older published releases and their assets. Preserve Git tags and commit history. Use `scripts/publish-release.ps1`.
- Keep local saves, settings, logs, credentials and build output out of source commits.
