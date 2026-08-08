# Diez Publishing Studio — current preview candidate

## 0.10.0-preview

- `.diez` schema 9 persists bibliographic Edition Metadata: title, subtitle, creator, language, publisher, ISBN and description.
- New projects initialize the edition title from the project name and language as `it`; migrated older projects receive the same compatibility defaults once.
- ISBN-10 and ISBN-13 are validated before metadata are saved.
- Edition Freeze now snapshots metadata together with Master and Bible; changing any of them supersedes the current freeze.
- Preflight blocks missing title/language and invalid ISBN, while a missing creator is reported as a warning.
- Publication Candidate remains immutable and can be created only from a current Edition Freeze with preflight READY.
- Publication ZIP now contains `master.txt`, `metadata.json`, `edition-manifest.json` and `preflight.txt`.
- Windows installer pipeline runs package, graph, Bible, consistency, revision, editable-master, edition-metadata, freeze, preflight and publication-candidate self-tests.
