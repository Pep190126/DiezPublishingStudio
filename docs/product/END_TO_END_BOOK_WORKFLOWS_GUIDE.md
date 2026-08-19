# Diez — workflow end-to-end dal progetto al libro finalizzato

Status: **GUIDA DI PRODOTTO / WORKING — DA AFFINARE CON UX REALE**

Scopo: spiegare come e perché si usano le aree di Diez per arrivare da un progetto vuoto a un output finalizzato. Questa guida non sostituisce le specifiche tecniche; descrive il percorso umano del publisher.

---

# 1. Mappa generale di Diez

La shell finale è organizzata in sei macroaree:

1. **Progetto** — apro/creo il `.diez`, aggiungo e verifico materiali.
2. **Tipo libro** — dico a Diez che prodotto editoriale sto costruendo.
3. **Produzione** — preparo contenuti, Prompt, Prompt Pack/AI e importo i Response.
4. **Controlli e revisione** — modifico il Master, controllo mappa/Bible e coerenza.
5. **Esportazione** — scelgo gli output del libro e i companion materiali/database.
6. **Libri finalizzati** — archivio e riapro le versioni congelate/esportate.

Principio comune:

`Progetto → Tipo libro → Produzione → Controlli/revisione → Freeze/Finalizzazione → Esportazione → Libreria finalizzati`

Non tutti i tipi libro usano ogni strumento nello stesso modo.

---

# 2. Cosa significa Progetto

## A cosa serve

È il contenitore persistente `.diez`.

Qui entrano:

- materiali utente;
- documenti/fonti;
- tabelle/dataset;
- immagini/reference;
- configurazione e decisioni;
- contenuti prodotti;
- Candidate AI;
- review e versioni.

## Workflow

1. Crea/apri progetto.
2. Assegna titolo/metadata minimi.
3. Aggiungi materiali.
4. Seleziona ogni materiale e verifica preview/struttura.
5. Per ZIP controlla l'elenco interno.
6. Rimuovi eventuali import errati.
7. Salva.

Perché: tutto ciò che succede dopo deve poter essere ricostruito dal progetto, senza dipendere da file sparsi non tracciati.

---

# 3. Coloring Book

La struttura precisa delle quattro fasi resta in revisione utente, ma il workflow funzionale è:

## Produzione

### Fase 1 — Definire cosa creare

- quantità tavole;
- pubblico/difficoltà;
- soggetti/personaggi;
- ambientazione;
- Scene;
- Consistent/Identity Profile;
- formato e specifiche.

### Fase 2 — Definire come deve apparire

- stile;
- Kawaii/Cozy/Bold & Easy quando applicabili;
- line weight;
- complessità/densità;
- sfondo/white space;
- HARD coloring.

### Fase 3 — Prompt e AI

1. Diez compila Prompt dalle scelte.
2. L'utente legge/modifica il Prompt.
3. Aggiunge reference/Identity Anchor se utili.
4. Crea Prompt Pack o copia il Prompt.
5. Genera immagini fuori/in provider.
6. Importa Response.

### Fase 4 — Controllo visuale

- preview Candidate;
- Vision;
- controllo identità/partecipanti/composizione;
- Approva / Scarta / Da rifare;
- correzione puntuale mantenendo identity anchor;
- `Porta nel libro` separato dall'approvazione.

## Controlli e revisione

- verifica quantità/placement;
- asset mancanti;
- Candidate non approvate usate per errore;
- consistency cross-lotto;
- eventuale layout/ordine finale.

## Esportazione

Possibili output:

- immagini finali;
- PDF/layout quando previsto;
- materiali utente ZIP;
- asset AI approvati ZIP;
- pacchetto completo di produzione.

---

# 4. Raccolta immagini

## Produzione

1. Definisci scopo della raccolta.
2. Definisci quantità/ordine/serie.
3. Definisci soggetti, ambienti, Scene e Consistent se servono.
4. Definisci rendering, colore, viewpoint, dettaglio, sfondo.
5. Compila Prompt generale + prompt per immagine.
6. Genera/importa Response.
7. Valuta la singola immagine e la coerenza della serie.
8. Approva/rigenera/riordina.

## Controlli

- immagini mancanti;
- doppioni;
- incoerenza di scala/viewpoint/stile;
- descrizioni/didascalie;
- ordine della raccolta.

## Esportazione

- immagini;
- descrizioni;
- layout interno/esterno/combinato quando previsto;
- materiali/reference;
- pacchetto completo.

---

# 5. Libro illustrato

## Produzione testo

1. Definisci pubblico e obiettivo.
2. Crea/importa struttura.
3. Edita parti/capitoli/pagine o nodi.
4. Genera/importa testo per le unità necessarie.
5. Modifica Candidate testo prima di applicarle al Master.

## Produzione immagini

Per ogni nodo:

1. Decidi se serve illustrazione.
2. Definisci scena e partecipanti.
3. Collega Consistent/Identity Profile.
4. Definisci inquadratura/relazione col testo.
5. Genera/importa Candidate.
6. Vision + approvazione.
7. Porta l'asset nella posizione editoriale.

## Controlli e revisione

Tab consigliati:

- **Master** — testo effettivo modificabile;
- **Mappa/Bible** — struttura, entità, relazioni, guida progetto;
- **Coerenza** — testo/immagini/scene/placement.

## Esportazione

- DOCX/PDF illustrato;
- asset AI approvati;
- materiali/reference;
- piano immagini/placement;
- pacchetto completo.

---

# 6. Romanzo / racconto

## Produzione

### 1. Bussola

Genere, pubblico, premessa, tono, POV, tempo verbale, lunghezza solo se nota.

### 2. Fondamenta

Conflitto, posta in gioco, arco, temi, finale, limiti.

### 3. Personaggi e mondo

Personaggi, relazioni, luoghi, timeline, fatti canonici/Bible.

### 4. Struttura

Editor:

`Parte → Capitolo → Scena`

con riordino, divisione/unione e stati.

### 5. Scrittura con AI

Per capitolo/scena:

1. seleziona l'unità;
2. controlla il contesto che Diez includerà;
3. compila/modifica Prompt;
4. genera;
5. importa Response;
6. modifica Candidate nel vero editor;
7. Approva;
8. Applica al Master.

## Controlli e revisione

- Master: editing umano finale;
- Mappa/Bible: personaggi, relazioni, timeline;
- Coerenza: POV, fatti, continuity, fili narrativi, ripetizioni.

## Esportazione

- DOCX/PDF;
- eventuali materiali/fonti/appendici;
- pacchetto completo se richiesto.

---

# 7. Saggio / manuale

## Produzione

1. Definisci cosa deve imparare/fare il lettore.
2. Aggiungi fonti/materiali.
3. Definisci policy delle fonti/citazioni.
4. Crea/importa indice.
5. Per ogni sezione stabilisci obiettivo, concetti, esempi, figure/tabelle.
6. Genera sezione per sezione.
7. Modifica Candidate e applica al Master.

## Controlli

- copertura dell'indice;
- terminologia;
- fatti/fonti;
- citazioni;
- ridondanze;
- figure/tabelle mancanti;
- leggibilità.

## Esportazione

- DOCX/PDF;
- tabelle/figure/asset;
- materiali/fonti quando devono accompagnare l'handoff;
- pacchetto completo.

---

# 8. Word Search

Il workflow resta quello dell'antesignano, potenziato dal Core Diez:

`DATABASE → FILTRI → GENERA → CONTROLLO → ESPORTA`

## DATABASE

1. Importa XLSX/CSV/TSV/TXT o adapter strutturato compatibile.
2. Verifica preview del dataset.
3. Mappa le colonne ai ruoli.
4. La grid si adatta allo schema.
5. Modifica/copia/ricerca record.

## FILTRI

1. Usa solo tassonomie realmente presenti.
2. Definisci pool.
3. Per Nostalgic usa anno/decade solo se mappati.
4. Puoi definire Scenario, es. `Pranzo di Natale`.
5. Puoi variare l'asse, es. `Pranzo di Natale × decade`.

## GENERA

1. Scegli puzzle, parole/puzzle, blocchi.
2. Genera dal pool.
3. Mantieni unicità whole-book.
4. Espandi ogni puzzle nella grid per vedere le parole.
5. Modifica tema/descrizione o sostituisci una singola parola.

## CONTROLLO

- duplicati/riusi whole-book;
- parole troppo lunghe;
- mismatch tassonomici/temporali;
- coerenza scena;
- sostituzione contestuale.

## ESPORTA

- Database completo XLSX;
- Database di questo libro XLSX;
- liste/manifest;
- Titans CSV;
- Titans XLSX;
- materiali ZIP quando presenti.

---

# 9. Cruciverba

## Produzione

1. Definisci tema/pubblico/difficoltà.
2. Importa o crea lessico.
3. Genera più Candidate di definizione per parola.
4. Scegli/modifica definizioni.
5. Controlla parole problematiche.

## Controlli

- risposta rivelata nella definizione;
- ambiguità;
- definizioni troppo simili;
- duplicati;
- caratteri/lunghezze;
- difficoltà.

## Esportazione

- parola/definizione;
- Qxw/handoff griglia quando previsto;
- database/lessico se necessario.

---

# 10. Quiz / trivia

## Produzione

1. Definisci pubblico, categorie, quantità, difficoltà.
2. Aggiungi fonti e cutoff temporale.
3. Genera domande per lotti/categorie.
4. Importa Response strutturato.
5. Modifica domanda, risposte, spiegazione e fonte.

## Controlli

- una sola risposta corretta quando richiesto;
- ambiguità;
- distrattori deboli;
- duplicati semantici;
- risposta supportata dalla fonte;
- distribuzione difficoltà/categorie.

## Esportazione

- dataset quiz;
- output editoriale/layout;
- fonti/provenienza quando richieste.

---

# 11. Catalogo / raccolta dati

## Produzione

1. Definisci cosa raccogliere.
2. Disegna/importa schema.
3. Definisci fonti e perimetro.
4. Importa/genera record.
5. Review dei Candidate.
6. Applica al dataset canonico.

## Controlli

- schema;
- campi mancanti;
- duplicati;
- normalizzazione;
- conflitti;
- provenance.

## Esportazione

- dataset completo;
- dataset filtrato/edizione;
- XLSX/CSV e altri adapter;
- provenance/manifest.

---

# 12. Perché esistono Master, Mappa/Bible e Coerenza

Queste tre funzioni non sono tre modi diversi di vedere la stessa cosa.

## Master

È **il contenuto che finirà davvero nel libro**. Qui l'utente modifica il testo definitivo o i contenuti applicati.

## Mappa contenuti / Bible

È **la memoria strutturata del progetto**: chi sono i personaggi, quali sezioni esistono, quali relazioni/fatti devono essere mantenuti, quali elementi sono collegati.

Serve perché un Prompt di una scena al capitolo 20 non può dipendere soltanto dal testo scritto nella casella corrente.

## Controllo coerenza

È **il revisore**: confronta ciò che il libro contiene con le regole/fatti/struttura e segnala incongruenze. Non deve modificare automaticamente il Master senza decisione dell'utente.

---

# 13. Perché esistono Approva e Porta/Applica nel libro

Sono due decisioni diverse.

**Approva** = il risultato AI è valido come Candidate.

**Porta/Applica nel libro** = questa Candidate diventa davvero il contenuto/asset usato nell'edizione.

Separarle consente:

- confrontare più Candidate approvate;
- non distruggere il Master durante la review;
- mantenere storia/provenienza;
- cambiare scelta prima del freeze.

---

# 14. Freeze / versione finalizzata

Prima dell'export definitivo Diez deve congelare una fotografia coerente dell'edizione.

Il freeze serve a sapere:

- quali contenuti erano approvati;
- quali asset erano usati;
- quale versione del database/struttura;
- quali controlli erano stati superati;
- quali file sono stati esportati.

Se dopo il freeze il progetto cambia, l'edizione congelata non deve fingere di essere ancora corrente.

---

# 15. Checklist universale prima dell'export

1. Materiali importati verificati.
2. Tipo libro corretto.
3. Scelte principali completate o consapevolmente lasciate `Later/Propose`.
4. Response importanti importati.
5. Candidate necessarie approvate.
6. Candidate corrette applicate al libro.
7. Master/struttura verificati.
8. Controlli HARD senza fail bloccanti.
9. Asset/record mancanti risolti.
10. Salvataggio progetto.
11. Freeze/Publication Candidate corrente.
12. Export principale + companion necessari.
13. Registrazione in Libri finalizzati.

## Principio finale

Diez non è soltanto un generatore di Prompt. È un percorso editoriale che conserva **decisioni → produzione → Candidate → revisione → contenuto applicato → edizione congelata → handoff**, adattando il metodo al tipo di libro.
