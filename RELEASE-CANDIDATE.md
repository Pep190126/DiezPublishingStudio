# Diez Publishing Studio — current preview candidate

## 0.13.0-preview

- Product direction is now explicit: Diez prepares editable production handoff material; it does not generate PDF or EPUB final-output workflows.
- EPUB export service, desktop UI and self-test have been removed from the product.
- `.diez` schema remains 9; Edition Metadata, Edition Freeze and immutable Publication Candidate continue unchanged.
- The desktop exposes one `Export / Handoff` center instead of separate final-format export buttons.
- DOCX remains the primary editable editorial handoff and is exported only from a current Publication Candidate with preflight READY.
- New CSV Master export writes UTF-8 structured editorial rows with order, source material, content kind, title, full editable text and source locator.
- New XLSX Master export writes a real Office Open XML workbook. Long Master bodies are split across numbered parts to stay below spreadsheet cell limits without losing text.
- New `ZIP immagini originali` export copies only image materials embedded in the `.diez`, in project order, byte-for-byte after extraction. The archive deliberately contains no manifest, metadata or other accessory files.
- Image ZIP export is independent of Edition Freeze / Publication Candidate so image-only and coloring-book projects can export their original assets without requiring a textual Master.
- Image ZIP export performs no resize, DPI rewrite, recompression or upscale. Future visual-generation settings will control requested pixel size/DPI at generation time instead of altering originals during handoff.
- CSV/XLSX editorial handoff is blocked when the Publication Candidate is missing or stale, matching the existing DOCX safety boundary.
- Windows installer pipeline runs package, graph, Bible, consistency, revision, editable-master, edition-metadata, freeze, preflight, publication-candidate, DOCX-export and handoff-export self-tests.
