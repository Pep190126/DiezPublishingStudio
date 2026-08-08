# Diez Publishing Studio — current preview candidate

## 0.11.0-preview

- `.diez` schema 9 continues to persist bibliographic Edition Metadata: title, subtitle, creator, language, publisher, ISBN and description.
- Edition Freeze snapshots metadata together with Master and Bible; changing any of them supersedes the current freeze.
- Publication Candidate remains immutable and can be created only from a current Edition Freeze with preflight READY.
- Publication ZIP continues to contain `master.txt`, `metadata.json`, `edition-manifest.json` and `preflight.txt`.
- New EPUB export creates a reflowable EPUB 3.3 container only from a current Publication Candidate.
- EPUB output includes the uncompressed `mimetype` entry, `META-INF/container.xml`, `EPUB/package.opf`, `EPUB/nav.xhtml`, stylesheet and XHTML content documents in reading order.
- EPUB package metadata includes persistent project UUID, title, language, modification timestamp and optional creator, publisher, description/subtitle and ISBN.
- Editing Master or Edition Metadata after Publication Candidate creation blocks EPUB export until a new Freeze, preflight and Publication Candidate are created.
- Windows installer pipeline runs package, graph, Bible, consistency, revision, editable-master, edition-metadata, freeze, preflight, publication-candidate and EPUB-export self-tests.
