# Diez Publishing Studio — 1.0 RC1 pre-finale

## 1.0.0-rc1

Questa è la build completa pre-finale destinata alla prova reale prima della versione 1.0 definitiva.

### Confine di prodotto

- Diez è il centro di gestione del progetto editoriale e la sorgente di verità nel file `.diez`.
- Diez prepara materiale completamente modificabile per Word, Publisher o un impaginatore esterno.
- Non esistono workflow di output finale PDF o EPUB.
- Canva non è una dipendenza del prodotto e non è usato come archivio del progetto.
- Lo schema `.diez` resta 10 e conserva originali incorporati, Master editabile, Content Graph/Bible, revisioni, metadati edizione, piano illustrazioni, Edition Freeze e Publication Candidate.

### Workflow editoriale completo

1. Crea o apri un progetto `.diez`.
2. Importa materiali: TXT, Markdown, CSV, XLSX, DOCX, ODT, RTF, PDF e immagini supportate.
3. Diez conserva gli originali nel pacchetto e costruisce la struttura editoriale.
4. Lavora sull'Editable Master senza sovrascrivere gli originali importati.
5. Rivedi Content Graph, Bible e problemi di coerenza.
6. Usa Revision Candidate per correzioni non distruttive e approvazioni esplicite.
7. Compila i metadati edizione e, per i libri illustrati, il Piano illustrazioni.
8. Crea Edition Freeze, esegui Preflight e crea il Publication Candidate corrente.
9. Effettua ogni consegna da `Export / Handoff`.

### Export / Handoff

- `DOCX editoriale`: documento Word modificabile; nei progetti illustrati incorpora le immagini pianificate come media DrawingML editabili/spostabili.
- `CSV Master`: Master strutturato UTF-8.
- `XLSX Master`: workbook Office Open XML reale, con segmentazione sicura dei testi oltre il limite di una cella.
- `ZIP immagini originali`: solo immagini, copiate byte-per-byte dal `.diez`, senza manifest, resize, ricompressione, modifica DPI o upscale. È indipendente dal Publication Candidate ed è il percorso dedicato anche ai coloring/image-only project.
- `Production Package`: consegna completa candidate-gated per Word, Publisher o impaginatore esterno.

### Production Package

- `manuscript/` contiene il DOCX editabile.
- `data/` contiene CSV e XLSX del Master.
- `assets/images/` contiene tutti gli originali immagine byte-per-byte.
- `handoff/illustration-plan.csv` contiene il piano di collocazione modificabile.
- `handoff/edition-metadata.json` contiene metadati e identità di Publication Candidate / Edition Freeze.
- `handoff/README-HANDOFF.txt` spiega il contratto di consegna e lascia tipografia, font, griglia, margini e rendering finale all'impaginatore.
- `handoff/manifest.json` inventaria ogni payload con percorso, dimensione, ruolo e SHA-256.
- Un'immagine usata nel libro illustrato compare intenzionalmente sia nel DOCX per il lavoro immediato sia come originale separato per l'uso professionale.

### Pulizia pre-finale

- La UI usa l'identità unica `1.0 RC1 Pre-finale` nell'area principale di lavoro.
- La barra superiore viene compattata per mantenere visibili Nuovo/Apri/Importa/Rimuovi/Salva/Edizione/Export senza overflow.
- `Edizione / Preflight` termina al Publication Candidate: il vecchio ZIP tecnico non è più presentato come export concorrente.
- Tutti gli output utente sono concentrati in `Export / Handoff`.

### Validazione richiesta per la promozione RC1

La pipeline Windows deve superare restore, build Release, publish self-contained x64, l'intera suite self-test del pacchetto `.diez`, struttura/graph/Bible/consistency/revisioni, Editable Master, metadati, Freeze/preflight/candidate, DOCX illustrato, CSV/XLSX, ZIP immagini, Production Package e relativi controlli d'integrità. Deve inoltre costruire l'installer e superare installazione, clean upgrade e disinstallazione.

Dopo questa build non vengono aggiunte nuove funzioni prima della prova utente: gli interventi successivi saranno guidati dai test reali della RC1.
