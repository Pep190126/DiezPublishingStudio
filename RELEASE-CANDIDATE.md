# Diez Publishing Studio — current preview candidate

## 0.15.0-preview

- Product direction remains editable production handoff: Diez does not generate PDF or EPUB final-output workflows.
- `.diez` schema remains 10. The existing Edition Metadata, Editable Master, Bible, Illustration Placement Plan, Edition Freeze and Publication Candidate remain the source of truth.
- Export / Handoff adds `Crea Production Package`, a candidate-gated ZIP intended for Word, Publisher or an external layout professional.
- A Production Package is created only from a current Publication Candidate with preflight READY and from a saved `.diez` package.
- `manuscript/` contains the editable DOCX. If the project has a valid Illustration Placement Plan, the DOCX includes the planned illustrations as editable Word/DrawingML objects.
- `data/` contains the editable Master in both UTF-8 CSV and Office Open XML XLSX.
- `assets/images/` contains every original image material embedded in the `.diez`, copied byte-for-byte without resize, recompression, DPI rewrite or upscale.
- `handoff/illustration-plan.csv` gives the layout professional an editable placement reference with image name, target chapter/section, human-readable position, width percentage and caption.
- `handoff/edition-metadata.json` contains the bibliographic edition metadata together with Publication Candidate and Edition Freeze identity.
- `handoff/README-HANDOFF.txt` explains the package structure and explicitly states that Diez is handing off editable production material rather than imposing final typography, fonts, grid, margins or page rendering.
- `handoff/manifest.json` inventories every payload file with path, byte size, SHA-256 and role so the delivered material can be checked for integrity.
- Images intentionally appear twice when used in an illustrated book: once incorporated into the DOCX for immediate editable layout work, and once as untouched originals in `assets/images/` for replacement or professional image handling.
- The dedicated coloring/image-only `ZIP immagini originali` remains unchanged and still contains only images; it is independent of Edition Freeze / Publication Candidate and carries no manifest or accessory files.
- Standalone DOCX, CSV and XLSX exports remain available for users who do not need the complete Production Package.
- Production Package self-test verifies the full folder contract, DOCX/CSV/XLSX presence, byte-identical original image asset, byte-identical media inside the nested DOCX, illustration-plan content, manifest metadata, stale-candidate blocking, and the absence of PDF/EPUB entries.
- Windows installer pipeline validates schema 10 persistence/migration, the editorial lifecycle, illustrated DOCX, standalone handoff exports, Production Package integrity, installer generation, clean upgrade and uninstall.
