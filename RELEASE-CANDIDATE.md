# Diez Publishing Studio — current preview candidate

## 0.9.0-preview

- Editable Master remains non-destructive for imported source materials.
- Edition Freeze snapshots Master and Bible and remains persisted in the `.diez` package.
- Preflight exposes blocking checks and warnings before publication.
- Publication Candidate can be created only from a current Edition Freeze with preflight READY.
- Publication Candidate is immutable and becomes superseded when the Master changes.
- A current Publication Candidate can export a ZIP editorial package containing `master.txt`, `edition-manifest.json` and `preflight.txt`.
- Windows installer pipeline runs package, graph, Bible, consistency, revision, editable-master, freeze, preflight and publication-candidate self-tests.
