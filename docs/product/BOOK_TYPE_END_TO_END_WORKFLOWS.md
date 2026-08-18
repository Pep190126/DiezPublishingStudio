# Diez — workflow end-to-end per Tipo libro

Status: **GUIDA DI PRODOTTO / WORKING — I PASSI COLORING RESTANO DA RAFFINARE CON L'UTENTE**

Data: 2026-08-19

Scopo: spiegare come si arriva da un progetto vuoto a un libro finalizzato ed esportato e, soprattutto, **perché** esistono le diverse aree di Diez.

Le dieci famiglie canoniche correnti sono:

- Coloring book;
- Raccolta immagini;
- Libro illustrato;
- Saggio / manuale;
- Word Search;
- Cruciverba;
- Quiz / trivia;
- Romanzo / racconto;
- Catalogo / raccolta dati;
- Altro.

---

# 1. La mappa mentale comune di Diez

Indipendentemente dal Tipo libro, la sidebar globale è:

`Progetto → Tipo libro → Produzione → Controlli e revisione → Esportazione → Libri finalizzati`

## Progetto — “Con cosa sto lavorando?”

Qui si crea/apre il `.diez`, si aggiungono materiali, si verificano con anteprima e si conserva la fonte di verità del lavoro.

**Perché serve:** impedisce che prompt, file AI, documenti e reference vivano sparsi senza collegamento.

## Tipo libro — “Che cosa sto costruendo?”

Seleziona la famiglia editoriale. Da questa scelta Diez decide quali strumenti e decisioni hanno senso.

**Perché serve:** un Romanzo non ha bisogno di `numero immagini`; un Coloring sì. Un Word Search usa un database; un Saggio usa fonti e struttura.

## Produzione — “Come costruisco il contenuto?”

È il percorso guidato specifico della famiglia. Qui si prendono decisioni, si costruisce il Prompt, si produce tramite AI o manualmente e si importano Response.

**Perché serve:** il Prompt è il risultato di un metodo editoriale, non una casella magica.

## Controlli e revisione — “Quello che ho prodotto è davvero pronto?”

Raggruppa:

- Testo principale modificabile;
- Mappa contenuti + Guida progetto;
- Controllo coerenza;
- review specifiche del Tipo libro.

**Perché serve:** una Candidate AI non deve diventare automaticamente contenuto finale.

## Esportazione — “Che cosa consegno?”

Crea l'edizione finale e, quando serve, materiali ZIP, asset AI, database, manifest e handoff specializzati.

## Libri finalizzati — “Quale edizione ho effettivamente prodotto?”

Conserva riferimenti a freeze/output/versioni finalizzate, per distinguere il progetto vivo dalle edizioni già consegnate.

---

# 2. Concetti AI comuni

## Prompt

Istruzioni leggibili che descrivono cosa produrre. Nasce dalle scelte del progetto e può essere modificato consapevolmente.

## Prompt Pack

Pacchetto che porta uno o più Prompt, Work Unit e reference a una AI esterna mantenendo identità e ordine.

**Quando usarlo:** quando si producono molte unità o servono asset/reference coordinati.

## Response

Risposta restituita dall'AI. Importarla significa soltanto collegarla al progetto.

## Candidate

Una versione possibile del risultato. Non è ancora il libro.

## Approva

Dichiara che la Candidate ha superato il controllo richiesto.

## Porta/Applica nel libro

Passaggio separato che modifica il contenuto editoriale canonico.

## Da rifare

Mantiene l'identità del lavoro, aggiunge correzioni e genera una nuova versione senza cancellare lo storico.

---

# 3. Coloring book

> Nota: l'organizzazione definitiva delle quattro fasi è ancora in revisione con l'utente. Questo workflow descrive la funzione, non congela i nomi delle pagine.

## A. Progetto

1. Crea/apri `.diez`.
2. Aggiungi eventuali reference, esempi, immagini, brief.
3. Verifica ogni materiale in anteprima.

## B. Tipo libro

4. Seleziona **Coloring book**.

## C. Produzione

5. Definisci il progetto visuale:
   - quantità tavole;
   - pubblico/difficoltà;
   - soggetti;
   - ambientazioni;
   - eventuali personaggi Consistent;
   - Scene e partecipanti.
6. Definisci il linguaggio visuale/colorabilità:
   - stile;
   - Kawaii quando scelto;
   - Cozy;
   - Bold & Easy;
   - line weight;
   - densità/complessità;
   - sfondo/white space;
   - HARD B/N/colorabilità.
7. Se un personaggio deve ricomparire, definisci Consistent e reference master.
8. Controlla il Prompt compilato.
9. Correggi eventuali note manuali.
10. Copia Prompt oppure crea Prompt Pack.
11. Genera con AI.
12. Importa Response.

## D. Controlli e revisione

13. Guarda ogni Candidate in preview grande.
14. Esegui Vision:
   - stile;
   - identità personaggio;
   - partecipanti;
   - B/N;
   - colorabilità;
   - anatomia/disegno;
   - no testo/watermark;
   - altri HARD.
15. Per errori: `Da rifare`, specifica la correzione e genera una nuova versione mantenendo identità/reference.
16. Approva Candidate corrette.
17. `Porta nel libro` nelle posizioni editoriali.
18. Controlla quantità, mancanti, doppioni e placement.

## E. Esportazione

19. Preflight/freeze.
20. Esporta output principale.
21. Facoltativamente:
   - Materiali ZIP;
   - Asset approvati ZIP;
   - manifest posizione → immagine.
22. Registra l'edizione in Libri finalizzati.

---

# 4. Raccolta immagini

## A. Progetto / Tipo libro

1. Crea/apri progetto.
2. Importa reference/materiali e verificali.
3. Scegli **Raccolta immagini**.

## B. Produzione

4. Definisci lo scopo: editoriale, didattico, tecnico, artistico ecc.
5. Definisci quantità e se è una serie coerente.
6. Definisci soggetti, ambienti, Scene e Consistent quando servono.
7. Scegli rendering, colore, dettaglio, sfondo, viewpoint e coerenza di serie.
8. Definisci ordine e descrizioni/didascalie se necessarie.
9. Controlla Prompt globale e Prompt per immagine.
10. Prompt Pack/copia → AI → Response.

## C. Revisione

11. Gallery + preview.
12. Controlla coerenza della serie e contenuto di ogni immagine.
13. Correggi singole unità senza rigenerare tutto.
14. Approva e riordina.

## D. Finalizzazione

15. Scegli layout: immagini sole, griglia, immagine+descrizione, sequenza ecc.
16. Freeze.
17. Esporta:
   - raccolta finale;
   - immagini approvate ZIP;
   - descrizioni;
   - materiali/reference se richiesti;
   - manifest.
18. Registra in Libri finalizzati.

---

# 5. Libro illustrato

## A. Progetto

1. Importa testo/materiali/reference esistenti.
2. Verifica anteprime.
3. Seleziona **Libro illustrato**.

## B. Produzione — struttura

4. Definisci pubblico e obiettivo.
5. Crea/importa/proponi la struttura del libro.
6. Nell'editor ad albero organizza parti, capitoli/sezioni e pagine/nodi.
7. Scrivi o importa il testo/brief di ogni nodo.

## C. Produzione — piano visuale

8. Per ogni nodo indica se serve un'illustrazione.
9. Definisci scena, partecipanti, scopo, inquadratura e relazione testo/immagine.
10. Definisci Consistent/reference.

## D. AI

11. Genera separatamente ciò che manca:
   - testo;
   - immagini.
12. Importa Response.

## E. Controlli

13. Revisiona testo nell'Editable Master.
14. Vision sulle immagini.
15. Controlla coerenza testo ↔ immagine.
16. Approva e applica Candidate.
17. Verifica che ogni posizione richiesta abbia contenuto/asset.

## F. Esportazione

18. Freeze.
19. Esporta documento impaginato.
20. A corredo: asset approvati, materiali, descrizioni e manifest.
21. Registra l'edizione.

---

# 6. Romanzo / racconto

## A. Progetto / Tipo libro

1. Importa eventuali appunti, manoscritti, outline o reference.
2. Seleziona **Romanzo / racconto**.

## B. Produzione — Bussola

3. Definisci genere, pubblico, premessa, tono, POV, tempo verbale e promessa al lettore.
4. Per lunghezza/capitoli scegli consapevolmente: definito, proponilo, derivarlo, più avanti.

## C. Fondamenta

5. Definisci conflitto, posta in gioco, arco, temi, finale e limiti.

## D. Personaggi e mondo

6. Crea personaggi, relazioni, luoghi, timeline e fatti canonici.
7. La Guida progetto/Bible diventa la memoria di coerenza.

## E. Struttura

8. Crea/importa o fai proporre un outline.
9. Organizza Parti → Capitoli → Scene.
10. Per ogni scena: obiettivo, POV, luogo/tempo, partecipanti, beat.

## F. Scrittura

11. Scegli l'unità: capitolo o scena.
12. Controlla il Prompt con il contesto necessario.
13. Genera/copia Prompt Pack.
14. Importa Candidate testo.

## G. Revisione

15. Leggi e modifica la Candidate nell'editor.
16. Confronta versioni.
17. Approva oppure `Da rifare`.
18. Applica al Testo principale modificabile solo dopo approvazione.
19. Controlla Bible, timeline, POV, relazioni, ripetizioni e fili aperti.

## H. Esportazione

20. Preflight dell'intero manoscritto.
21. Freeze.
22. Esporta DOCX/PDF/TXT e, se richiesto, materiali editoriali a corredo.
23. Registra l'edizione.

---

# 7. Saggio / manuale

## A. Progetto

1. Importa fonti, appunti, documenti, tabelle e immagini.
2. Verifica l'intake.
3. Seleziona **Saggio / manuale**.

## B. Obiettivo e fonti

4. Definisci cosa deve sapere/fare il lettore.
5. Definisci pubblico e profondità.
6. Marca fonti obbligatorie, vietate, citazioni e terminologia.

## C. Struttura

7. Crea/importa/proponi indice: parti, capitoli, sezioni, esercizi, box, appendici.
8. Per ogni sezione definisci obiettivo, concetti, fonti, esempi e figure.

## D. Produzione

9. Genera per sezione/capitolo con Prompt grounded sulle fonti appropriate.
10. Importa Candidate.

## E. Revisione

11. Modifica il testo.
12. Controlla completezza rispetto all'indice.
13. Controlla fatti, fonti, terminologia, citazioni, ridondanze.
14. Revisiona figure/tabelle e placement.
15. Approva e applica.

## F. Esportazione

16. Preflight/freeze.
17. Esporta documento finale.
18. A corredo: figure, tabelle, materiali selezionati, provenance/bibliografia quando previsto.
19. Registra l'edizione.

---

# 8. Word Search

Il metodo deriva dall'antesignano ed è deliberatamente diverso dagli altri:

`DATABASE → FILTRI → GENERA → CONTROLLO → ESPORTA`

## A. Progetto / database

1. Crea/apri progetto e scegli **Word Search**.
2. Importa database XLSX/CSV/TSV/TXT tramite adapter strutturato.
3. Verifica intestazioni e prime righe.
4. Mappa ruoli: parola, ID, tassonomie, rilevanza, KDPSAFE, eventuali assi temporali ecc.
5. La griglia si adatta alle colonne reali.
6. Correggi/aggiungi/elimina record mantenendo colonne extra.

**Perché:** il database è la materia prima del libro.

## B. FILTRI

7. Definisci il pool con tassonomie, sicurezza, rilevanza e altri assi presenti.
8. Per Nostalgic puoi definire scenari come `Pranzo di Natale × decade`.
9. Per altri temi usa gli assi appropriati (`Habitat`, `Regione`, `Stagione`...).
10. Controlla quanti record restano disponibili.

**Perché:** GENERA deve scegliere da un universo intenzionale, non da tutto il database.

## C. GENERA

11. Definisci numero puzzle, parole/puzzle, blocchi e policy riuso.
12. Genera liste bilanciate.
13. L'AI, se usata, lavora dentro le regole/dataset e non sostituisce la fonte canonica.
14. Espandi ogni puzzle per vedere le parole.

## D. CONTROLLO

15. Verifica whole-book:
   - duplicati/riusi;
   - lunghezze;
   - scena/variante;
   - coerenza tassonomica;
   - pool insufficiente.
16. Sostituisci chirurgicamente singole parole e rivalida tutto il libro.
17. Approva i puzzle.

## E. ESPORTA

18. Esporta, secondo necessità:
   - **Database completo XLSX**;
   - **Database del libro XLSX**;
   - puzzle XLSX;
   - puzzle CSV;
   - Self-Publishing Titans XLSX/CSV;
   - manifest;
   - eventuali materiali ZIP.
19. Freeze/registra edizione.

---

# 9. Cruciverba

## A. Progetto / lessico

1. Scegli **Cruciverba**.
2. Importa o costruisci il lessico.
3. Definisci tema, lingua, pubblico, difficoltà.

## B. Produzione

4. Prepara parole candidate.
5. Per ogni parola genera/scrivi più Candidate di definizione.
6. Modifica manualmente le definizioni.

## C. Controllo

7. Verifica:
   - risposta rivelata nella definizione;
   - ambiguità;
   - duplicati;
   - definizioni troppo simili;
   - caratteri/lunghezza;
   - difficoltà.
8. Approva coppie parola/definizione.

## D. Handoff/finalizzazione

9. Esporta formato per il costruttore di griglia quando applicabile (es. Qxw) e/o dataset parole/definizioni.
10. Conserva database/materiali a corredo se richiesto.
11. Registra l'edizione.

---

# 10. Quiz / trivia

## A. Definizione

1. Scegli **Quiz / trivia**.
2. Definisci pubblico, scopo, categorie, quantità, difficoltà, numero opzioni e fonti.
3. Definisci cutoff temporale quando i fatti possono cambiare.

## B. Produzione

4. Prompt Pack per blocchi di domande.
5. Importa Response strutturato.

## C. Controllo

6. Per ogni domanda controlla:
   - una sola risposta corretta quando richiesto;
   - distrattori credibili;
   - ambiguità;
   - supporto della risposta;
   - duplicati semantici;
   - difficoltà.
7. Modifica/rigenera singole domande.
8. Approva.

## D. Esportazione

9. Esporta dataset XLSX/CSV e/o documento impaginato.
10. Aggiungi fonti/provenance se previsto.
11. Freeze e registra.

---

# 11. Catalogo / raccolta dati

## A. Scopo e schema

1. Scegli **Catalogo / raccolta dati**.
2. Definisci cosa raccogliere e il perimetro.
3. Crea lo schema dei campi: tipo, obbligatorietà, descrizione, normalizzazione.

## B. Fonti

4. Importa materiali/dataset.
5. Definisci provenance e fonti ammesse.

## C. Produzione

6. Raccogli/genera record in lotti.
7. Importa Response strutturati.

## D. Revisione

8. Valida schema.
9. Gestisci mancanti, duplicati, conflitti e normalizzazione.
10. Verifica provenance.
11. Approva/applica record.

## E. Export

12. Esporta XLSX/CSV ricco.
13. Esporta schema/manifest/provenance e asset collegati quando presenti.
14. Freeze/registra versione.

---

# 12. Altro

**Altro** è un fallback controllato, non un contenitore senza regole.

1. Definisci obiettivo.
2. Definisci l'unità del risultato.
3. Definisci struttura/quantità se applicabili.
4. Definisci output atteso e criteri di review.
5. Produci tramite Prompt comune.
6. Importa Candidate.
7. Revisiona/applica.
8. Definisci export.

Se un pattern viene usato spesso, deve diventare in futuro un vero profilo di famiglia invece di accumulare logica speciale dentro `Altro`.

---

# 13. Esempio: perché “Mappa contenuti + Guida progetto” esiste

Questa area non serve a produrre direttamente un file.

Serve a conservare ciò che deve restare vero attraverso molte unità e molte sessioni:

- personaggi;
- relazioni;
- luoghi;
- fatti;
- tassonomie;
- regole del mondo;
- identità visuali;
- provenance.

Esempio Romanzo: evita che un personaggio cambi età o relazione senza motivo.

Esempio Coloring/Illustrato: conserva identità Consistent oltre il singolo batch.

Esempio Manuale: conserva terminologia e concetti canonici.

---

# 14. Esempio: perché “Testo principale modificabile” è separato dalla Response

La Response è ciò che l'AI ha proposto.

Il Testo principale modificabile è ciò che **il libro sta realmente diventando**.

La separazione consente di:

- confrontare versioni;
- correggere manualmente;
- scartare una Candidate senza perdere il Master;
- sapere cosa è già stato applicato;
- mantenere originali e storico.

---

# 15. Esempio: perché esiste il freeze

Un progetto continua a cambiare. Un'edizione consegnata no.

Il freeze identifica esattamente:

- testo;
- immagini;
- puzzle/dati;
- metadata;
- asset;

che appartengono a una determinata esportazione.

Se il giorno dopo si corregge qualcosa, si crea una nuova edizione/freeze invece di rendere ambiguo quale versione sia stata consegnata.

---

# 16. Regola finale

Il workflow Diez deve sempre permettere al publisher di rispondere a cinque domande:

1. **Cosa sto costruendo?**
2. **Con quali materiali e decisioni?**
3. **Cosa ho chiesto/prodotto?**
4. **Cosa ho realmente approvato e portato nel libro?**
5. **Quale versione ho finalizzato e consegnato?**

Se una funzione non aiuta a rispondere a una di queste domande, deve essere ripensata, nascosta o spostata nel punto del workflow in cui acquista senso.