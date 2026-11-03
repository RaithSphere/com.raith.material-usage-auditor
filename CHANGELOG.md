# Changelog

## Unreleased

- Added Editor texture memory reporting with unavailable-value handling and deduplicated totals.
- Added material sorting and filters, plus timestamped CSV exports with a unique-texture summary.
- Added CI checks for package metadata, release tags, and archive contents before publishing.

## 1.0.0 - 2026-08-30

- Added root GameObject scanning across active and inactive child renderers.
- Added material usage, texture property, source-size, imported-size, and effective Max Size reporting.
- Added filtering, large-texture highlighting, ping/select actions, and CSV export.
- Added an explicit Select action on every texture row for quick access to Import Settings.
- Added EditMode regression coverage for inactive renderers, shared assets, null slots, and non-imported textures.
