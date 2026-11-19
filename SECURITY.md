# Security policy

## Supported versions

Only the latest release of each plugin receives fixes: the latest stable
[release](https://github.com/PetitCastor/ocrx-plugins/releases/latest) for SignaturePlugin, and the
latest preview tag for Item Tracker. The OCRX engine offers plugin updates when they are published,
so fixes reach users through a new release.

## Reporting a vulnerability

Please do **not** open a public issue for a security problem.

Report it privately through GitHub instead: open the repository's **Security** tab and choose
**Report a vulnerability**, or go directly to
[this form](https://github.com/PetitCastor/ocrx-plugins/security/advisories/new). Only the
maintainer can see the report.

Please include:

- the plugin and its version, and the OCRX engine version (both shown in the OCRX window: tray
  icon, **Show OCRX**),
- what an attacker could do and under which conditions,
- steps or a proof of concept to reproduce it.

You should get a first reply within a week. Once a fix is released, the advisory is published and
you are credited unless you prefer otherwise.

## Scope

In scope: the plugins published from this repository and what they ship with — their release zips,
the `plugins.json` and `plugins.preview.json` catalogs the engine installs from, the files the
plugins read and write under `%LOCALAPPDATA%\OCRX`, and Item Tracker's handling of `Game.log`.

The OCRX engine and the `Ocrx.*` SDK packages are maintained by the same person: a problem you
find in them through a plugin can be reported here too, and it will be routed to the right place.

Out of scope: Star Citizen, the RSI Launcher, Windows OCR and GitHub themselves. Report problems
with those to their owners.
