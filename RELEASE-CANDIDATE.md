# Diez Publishing Studio — current preview candidate

## 0.12.0-preview

- `.diez` schema 9 continues to persist bibliographic Edition Metadata: title, subtitle, creator, language, publisher, ISBN and description.
- Edition Freeze snapshots metadata together with Master and Bible; changing any of them supersedes the current freeze.
- Publication Candidate remains immutable and can be created only from a current Edition Freeze with preflight READY.
- Publication ZIP continues to contain `master.txt`, `metadata.json`, `edition-manifest.json` and `preflight.txt`.
- EPUB export continues to create a reflowable EPUB 3.3 container only from a current Publication Candidate.
- New DOCX export creates an Office Open XML editorial document only from a current Publication Candidate.
- DOCX output contains package relationships, document core properties, styles and a WordprocessingML document with edition title page, optional subtitle/creator/publisher/ISBN, page break and chapter headings/content in reading order.
- DOCX core properties carry title, optional subtitle/creator/description, language, persistent project identifier or ISBN, and the Publication Candidate timestamp.
- Editing Master or Edition Metadata after Publication Candidate creation blocks both EPUB and DOCX export until a new Freeze, preflight and Publication Candidate are created.
- Windows installer pipeline runs package, graph, Bible, consistency, revision, editable-master, edition-metadata, freeze, preflight, publication-candidate, EPUB-export and DOCX-export self-tests.
