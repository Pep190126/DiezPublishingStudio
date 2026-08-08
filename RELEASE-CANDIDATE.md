# Diez Publishing Studio — current preview candidate

## 0.14.0-preview

- Product direction remains editable handoff: no PDF or EPUB final-output workflow is reintroduced.
- `.diez` schema advances to 10 and persists an Illustration Placement Plan alongside Edition Metadata, Master, Bible and revision history.
- Each illustration placement records the original image material, target chapter/section, placement position, indicative width and optional caption.
- Supported DOCX placement positions are before the chapter/section heading, after the heading, after the text, or on a dedicated page after the text.
- The Illustration Placement Plan is part of the canonical Edition Freeze snapshot. Changing image placement, width or caption after Freeze makes the Freeze and Publication Candidate stale.
- Preflight validates every planned illustration: referenced image and target content must exist, the original must be embedded, the position/width must be valid, and the image must use a DOCX-interoperable format supported by this preview.
- PNG, JPG/JPEG, GIF and BMP can be placed in the illustrated DOCX. Other imported image formats remain preserved in `.diez` and exportable in the original-images ZIP.
- DOCX export now embeds planned images as real WordprocessingML/DrawingML media relationships. The embedded media bytes are copied from the `.diez` original without recompression.
- DOCX illustrations are centered and sized from the stored width percentage while preserving aspect ratio and fitting within the document text area. Captions use a dedicated editable Word paragraph style.
- The Export / Handoff center adds `Piano illustrazioni`, where users can add, update or remove placements before creating the Edition Freeze / Publication Candidate.
- DOCX remains candidate-gated; CSV and XLSX Master exports remain candidate-gated; ZIP immagini originali remains independent so coloring/image-only projects can hand off original assets without a textual Master.
- ZIP immagini originali still contains only image files, byte-for-byte from the embedded originals, with no manifest, metadata, resize, DPI rewrite, recompression or upscale.
- Windows installer pipeline validates schema 10 migration/persistence, the existing editorial lifecycle, illustrated DOCX media embedding and stale-placement guard, CSV/XLSX handoff, byte-preserving image ZIP, installer generation and clean upgrade/uninstall.
