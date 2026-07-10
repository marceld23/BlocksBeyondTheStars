# Code Signing Policy

Windows release binaries of **Blocks Beyond the Stars** are digitally signed using a free code
signing certificate provided by the [**SignPath Foundation**](https://signpath.org), with a
certificate issued in the name of the SignPath Foundation.

## Official repository

The only official source of Blocks Beyond the Stars is:

<https://github.com/marceld23/BlocksBeyondTheStars>

Signed builds are published exclusively on this project's
[GitHub Releases](https://github.com/marceld23/BlocksBeyondTheStars/releases) and mirrored to the
official [itch.io page](https://jumavegames.itch.io/blocks-beyond-the-stars). Binaries obtained from
anywhere else are not covered by this policy and should not be trusted.

## What is signed, and how

- Only the **Windows installer artifacts** are signed: the per-user installer (`*Setup.exe`), the
  machine-wide MSI (`*.msi`), and the portable ZIP (`*Portable.zip`).
- Signing happens **only** inside the automated GitHub Actions release workflow
  ([`.github/workflows/release.yml`](.github/workflows/release.yml)), which builds exclusively from
  source in the public repository above. No locally or manually produced binary is ever signed.
- A signed release is produced only from a version tag (`vX.Y.Z`) pushed by a project maintainer;
  the same tag is the single source of truth for the version baked into the build.
- The Linux and (experimental) macOS builds are **not** covered by this certificate. The macOS
  build is unsigned and un-notarized by design — see the README security notices.

## Who may authorize a release

Releases are authorized and tagged by the project maintainer(s):

- **Marcel Dütscher** ([@marceld23](https://github.com/marceld23))

Maintainer GitHub accounts have two-factor authentication (2FA) enabled.

## Privacy

Code signing is performed by the SignPath service on the build artifacts described above; it does
not process end-user data. See the [SignPath Foundation privacy policy](https://signpath.org/privacy)
and this project's [privacy policy](https://www.blocksbeyondthestars.com/en/datenschutz).

## Acknowledgement

Free code signing on Windows is provided by [SignPath.org](https://signpath.org), certificate by the
[SignPath Foundation](https://signpath.org). Thank you for supporting open-source software. 🙏
