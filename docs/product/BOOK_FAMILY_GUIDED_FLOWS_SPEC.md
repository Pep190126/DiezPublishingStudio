# Diez — percorsi guidati per Tipo libro

Status: **SPECIFICA UX / PRODOTTO — WORKING, NON CONSOLIDATA**

Data: 2026-08-18

Branch: `spike/uno-platform-ui`

Documento collegato: `docs/product/PROMPT_SYSTEM_ARCHITECTURE_SPEC.md`

## 1. Obiettivo

Ogni Tipo libro deve offrire al publisher un **percorso di lavoro naturale**, non un elenco di campi tecnici.

Il percorso deve:

- insegnare implicitamente un metodo ripetibile;
- essere comprensibile senza conoscere Prompt Engineering;
- raccogliere soltanto decisioni che hanno senso per quel Tipo libro;
- permettere di non sapere ancora alcune decisioni;
- arrivare a un Prompt leggibile/modificabile;
- consentire copia manuale o Prompt Pack;
- gestire Response, revisione e applicazione al libro;
- conservare tutto nel `.diez`.

I passi descritti qui sono **passi del workspace**, non nuove macrovoci della sidebar.

---

# 2. Regole UX comuni

## 2.1 Ogni pagina deve rispondere a una domanda umana

Esempi validi:

- `Che libro vuoi costruire?`
- `Che cosa deve succedere nella storia?`
- `Quali immagini ti servono?`
- `Da quali parole possiamo costruire i puzzle?`
- `Quali dati vuoi raccogliere?`

Evitare pagine costruite intorno a concetti interni come Job, Work Unit, manifest o compiler.

## 2.2 Avanti/Indietro senza perdita di stato

Ogni passo salva automaticamente o rende evidente lo stato non salvato.

Tornare indietro non deve distruggere:

- testi;
- scene;
- struttura;
- scelte;
- Prompt editato;
- Response già importati.

## 2.3 Riepilogo vivo

È utile una colonna/pannello di riepilogo contestuale che mostri, in linguaggio editoriale:

- cosa è già deciso;
- cosa manca;
- cosa sarà chiesto all'AI;
- eventuali conflitti.

Non mostrare metadata tecnici.

## 2.4 “Non lo so ancora” è una scelta valida

Per struttura, pagine, capitoli, quantità indicative o altri valori non essenziali deve essere possibile proseguire dichiarando:

- `Lo decido io`;
- `Proponilo con AI`;
- `Derivalo dai materiali`;
- `Più avanti`.

---

# 3. Coloring Book

Il Coloring resta il riferimento più maturo per la pipeline visuale, ma i suoi quattro passi devono diventare un metodo editoriale più chiaro.

La struttura definitiva dei passi 1–4 resta **WORKING** perché l'utente sta raccogliendo note dalla build fisica Uno.

Direzione raccomandata:

1. **Idea e contenuto** — quantità, soggetto, pubblico, ambientazione, personaggi/soggetti, Scene;
2. **Linguaggio visivo e colorabilità** — stile, Kawaii, Cozy, Bold & Easy, line weight, difficoltà, densità, sfondo, aree colorabili, HARD;
3. **Prompt e produzione** — Prompt leggibile/editabile, reference, Prompt Pack/copia, import Response;
4. **Controllo e scelta** — preview, Vision, confronto Candidate, approvazione, `Porta nel libro`.

Nota: Scene e Consistent devono comparire prima della compilazione del Prompt e nel punto logico in cui l'utente sta definendo contenuto/personaggi, non come funzione tecnica separata.

---

# 4. Raccolta immagini

Può condividere gran parte dei componenti del Coloring, ma non le sue regole artistiche HARD.

## Passo 1 — Scopo della raccolta

Domande:

- A cosa servono le immagini?
- Quante ne servono?
- Sono una serie coerente o elementi indipendenti?
- Hanno un ordine?
- Devono avere descrizioni/didascalie?

Scelte tipiche:

- numero immagini;
- uso: editoriale, didattico, decorativo, tecnico, reference, altro;
- output singolo/serie;
- ordine libero / sequenza;
- descrizione associata sì/no;
- formato/orientamento/risoluzione.

## Passo 2 — Cosa rappresentano

Componenti riusabili dal Coloring:

- soggetti;
- ambienti;
- Scene;
- Consistent;
- reference/paradigmi.

Aggiunte specifiche:

- viewpoint/inquadratura;
- stile rendering;
- trattamento colore;
- uniformità della serie;
- testo dentro immagine solo se esplicitamente richiesto.

## Passo 3 — Prompt e generazione

- riepilogo della serie;
- Prompt generale;
- Prompt per immagine/unità;
- modifica/copia;
- Prompt Pack;
- import Response.

## Passo 4 — Revisione raccolta

- gallery + preview grande;
- confronto coerenza di serie;
- descrizioni modificabili;
- approva/scarta/rigenera singolarmente;
- riordina;
- controlla quantità/mancanti/doppioni;
- prepara layout/export.

---

# 5. Libro illustrato

È una combinazione di **contenuto testuale + piano visuale**, non una semplice raccolta immagini.

## Passo 1 — Progetto del libro

- pubblico;
- genere/tipo;
- obiettivo narrativo o informativo;
- struttura nota / da proporre;
- lunghezza indicativa opzionale;
- quantità testo per pagina/sezione;
- materiali già disponibili.

## Passo 2 — Struttura e contenuto

Editor ad albero:

- parti;
- capitoli/sezioni;
- pagine/nodi editoriali quando applicabile;
- testo o brief del nodo.

Azioni:

- aggiungi;
- elimina;
- duplica;
- rinomina;
- trascina/riordina;
- chiedi proposta AI;
- importa outline.

## Passo 3 — Piano illustrazioni

Per ogni posizione:

- serve immagine?;
- scopo dell'immagine;
- scena;
- soggetti partecipanti;
- inquadratura;
- relazione con il testo;
- reference;
- Consistent.

Qui si riusano i componenti visuali del Coloring/Raccolta immagini.

## Passo 4 — Prompt e produzione

Due famiglie di unità possibili:

- testo;
- immagini.

Il publisher può generare solo ciò che manca.

## Passo 5 — Revisione e impaginazione editoriale

- editor del testo restituito;
- Candidate visuali;
- Vision;
- coerenza testo/immagine;
- approva;
- `Porta nel libro`;
- verifica posizioni mancanti.

---

# 6. Romanzo / racconto

Il Romanzo richiede un workspace specifico. L'attuale forma `Outline + Note + Piano illustrazioni` è troppo generica.

## Passo 1 — Bussola del romanzo

Domande principali:

- Che storia stiamo raccontando?
- Per chi?
- Che genere/tono?
- Qual è la promessa al lettore?

Scelte:

- genere/sottogenere;
- pubblico;
- tono;
- POV;
- tempo verbale;
- lingua;
- lunghezza target opzionale.

Le quantità devono avere stato:

- parole definite / da proporre / più avanti;
- pagine definite / derivate / più avanti;
- capitoli definiti / da proporre / più avanti.

Non chiedere numero immagini salvo attivazione esplicita di un piano illustrazioni.

## Passo 2 — Fondamenta narrative

Editor parlanti:

- premessa;
- conflitto centrale;
- posta in gioco;
- arco principale;
- temi;
- cose da evitare;
- finale noto / da proporre.

Possibilità di chiedere all'AI **proposte**, senza applicarle automaticamente.

## Passo 3 — Personaggi e mondo

Componenti:

- personaggi;
- ruoli;
- obiettivi;
- conflitti;
- relazioni;
- luoghi;
- regole del mondo;
- timeline;
- Consistent/Bible.

Ogni entità ha identità stabile.

## Passo 4 — Struttura del libro

Editor dell'indice/outline ad albero.

Deve permettere:

- parti opzionali;
- capitoli;
- scene dentro capitolo;
- titolo modificabile;
- breve obiettivo/riassunto;
- POV della scena;
- personaggi presenti;
- luogo/tempo;
- stato (`da scrivere`, `bozza AI`, `revisionato`, `approvato`).

Azioni:

- aggiungi/rimuovi;
- sposta con drag & drop;
- dividi/unisci;
- rinumera automaticamente;
- genera proposta di struttura;
- confronta proposta con struttura esistente.

## Passo 5 — Prompt di scrittura

Il Prompt può essere compilato a livelli:

- Prompt del progetto;
- Prompt del capitolo;
- Prompt della singola scena.

L'utente vede sempre:

- contesto incluso;
- cosa l'AI deve produrre;
- continuità che verrà rispettata;
- limiti di lunghezza;
- eventuali note manuali.

## Passo 6 — Response ed editor testo

Il testo restituito dall'AI non viene applicato ciecamente.

Per ogni unità:

- Candidate;
- preview completa;
- editor reale;
- confronto con versione precedente;
- accetta/scarta;
- combina manualmente;
- salva come bozza;
- applica al Master.

Funzioni richieste:

- undo/redo;
- cerca/sostituisci;
- conteggio parole;
- note margine/revisione;
- stato del capitolo/scena.

## Passo 7 — Coerenza e finalizzazione

Controlli:

- nomi;
- relazioni;
- timeline;
- POV;
- informazioni note ai personaggi;
- fili narrativi aperti;
- ripetizioni;
- incongruenze con Bible.

Poi Editable Master → impaginazione → export.

---

# 7. Saggio / manuale

Condivide alcuni componenti long-form con Romanzo, ma il metodo è diverso.

## Passo 1 — Obiettivo e lettore

- cosa deve imparare/capire/fare il lettore;
- livello del pubblico;
- tono;
- profondità;
- risultato finale;
- lunghezza indicativa opzionale.

## Passo 2 — Fonti e vincoli

- materiali forniti;
- fonti obbligatorie;
- fonti vietate;
- necessità di citazioni;
- policy fattuale;
- glossario/terminologia.

## Passo 3 — Struttura

Editor indice:

- parti;
- capitoli;
- sezioni;
- box/esercizi/esempi;
- appendici.

L'AI può proporre la struttura sulla base dell'obiettivo e dei materiali.

## Passo 4 — Piano dei contenuti

Per ogni sezione:

- obiettivo;
- concetti da coprire;
- esempi;
- fonti;
- figure/tabelle necessarie;
- prerequisiti;
- livello di dettaglio.

## Passo 5 — Prompt / produzione

Generazione per sezione/capitolo, non necessariamente tutto il libro in una volta.

## Passo 6 — Revisione

- editor testo;
- fact/terminology consistency;
- completezza rispetto alla struttura;
- ridondanze;
- citazioni mancanti;
- figure mancanti;
- leggibilità.

---

# 8. Word Search

Il percorso deve attenersi alla specifica dell'antenato `WordSearchListManager` già conservata nel repository.

Sequenza canonica:

1. **DATABASE**
2. **FILTRI**
3. **GENERA**
4. **CONTROLLO**
5. **ESPORTA**

## 8.1 DATABASE

- import XLSX/CSV/TSV/TXT;
- mapping colonne flessibile;
- parola obbligatoria;
- ID/rilevanza/KDPSAFE/tassonomie opzionali;
- colonne extra preservate;
- ricerca;
- vai a ID;
- aggiunta con clonazione metadati;
- modifica/elimina;
- validazione duplicati e vuoti.

## 8.2 FILTRI

- due coppie tassonomiche dipendenti;
- valori derivati dal dataset;
- rilevanza minima;
- KDPSAFE;
- usata/non usata;
- ricerca ed esclusioni.

## 8.3 GENERA

- numero puzzle;
- parole/puzzle;
- puzzle/blocco;
- blocchi omogenei;
- riuso controllato solo se esplicito;
- anteprima;
- edit tema/descrizione/parola.

L'AI, se usata, deve lavorare sul **dataset filtrato e sulle regole correnti**, non inventare una seconda sorgente parole salvo scelta esplicita.

## 8.4 CONTROLLO

- parole troppo lunghe;
- duplicati/riusi whole-book;
- ricerca;
- evidenziazione;
- sostituzione chirurgica contestuale;
- revalidazione globale prima dell'applicazione.

## 8.5 ESPORTA

- database completo Diez reimportabile;
- liste;
- manifesto;
- Self Publishing Titans CSV/XLSX.

Il Prompt/AI non deve spezzare questa filosofia in un percorso parallelo: è uno strumento dentro GENERA/CONTROLLO.

---

# 9. Cruciverba

Obiettivo: preparare contenuti di qualità e un handoff affidabile al tool di costruzione griglia quando applicabile.

## Passo 1 — Tema e lessico

- lingua;
- tema;
- pubblico;
- difficoltà;
- quantità indicativa;
- fonte parole: manuale, dataset, AI, mista.

## Passo 2 — Parole candidate

Tabella editabile:

- parola;
- definizione;
- categoria;
- difficoltà;
- note;
- stato.

Azioni:

- importa;
- genera alternative;
- elimina duplicati;
- valida lunghezza/caratteri.

## Passo 3 — Definizioni

Per ogni parola:

- più Candidate di definizione;
- tono;
- livello di ambiguità;
- gioco di parole sì/no;
- verifica che la definizione non contenga la risposta.

## Passo 4 — Controllo

- duplicati;
- definizioni troppo simili;
- risposta rivelata;
- mismatch lingua;
- difficoltà sbilanciata;
- parole problematiche per la griglia.

## Passo 5 — Handoff

- export elenco parole/definizioni;
- profilo Qxw quando previsto;
- ritorno/import del risultato griglia se integrato in seguito.

---

# 10. Quiz / trivia

## Passo 1 — Progetto del quiz

- pubblico;
- scopo;
- categorie;
- quantità domande;
- risposte per domanda;
- difficoltà o distribuzione difficoltà;
- lingua.

## Passo 2 — Fonti e veridicità

- fonti/materiali;
- quanto può usare conoscenza generale;
- data cutoff se rilevante;
- temi da evitare;
- spiegazione risposta sì/no.

## Passo 3 — Struttura del set

- categorie e quote;
- difficoltà per categoria;
- ordine casuale/tematico/progressivo;
- eventuali round/sezioni.

## Passo 4 — Prompt / genera

Response strutturata per domanda:

- domanda;
- opzioni;
- risposta corretta;
- spiegazione;
- categoria;
- difficoltà;
- fonte/provenienza quando richiesta.

## Passo 5 — Controllo/editor

- duplicati semantici;
- più risposte potenzialmente corrette;
- risposta non supportata;
- distrattori deboli;
- difficoltà incoerente;
- testo ambiguo;
- editing manuale.

## Passo 6 — Export

Profili futuri specifici per piattaforma senza modificare il dataset canonico.

---

# 11. Catalogo / raccolta dati

## Passo 1 — Che cosa raccogliamo

- oggetto del catalogo;
- scopo;
- quantità indicativa opzionale;
- area geografica/temporale;
- criteri inclusione/esclusione.

## Passo 2 — Schema

Editor colonne/campi:

- nome;
- tipo;
- obbligatorio;
- descrizione;
- esempio;
- regole di normalizzazione.

Possibilità di importare schema da CSV/XLSX esistente.

## Passo 3 — Fonti e provenienza

- fonti ammesse;
- origine obbligatoria;
- data raccolta;
- affidabilità;
- note.

## Passo 4 — Prompt / raccolta

Generazione per lotti con schema vincolante.

## Passo 5 — Controllo dati

- schema validation;
- missing;
- duplicati;
- normalizzazione;
- conflitti;
- provenienza mancante;
- editing tabellare.

## Passo 6 — Export

- CSV;
- XLSX;
- JSON;
- profili custom;
- dataset Diez reimportabile.

---

# 12. Altro

`Altro` non deve essere un formulario generico senza guida.

Percorso minimo:

1. obiettivo;
2. unità che compongono il risultato;
3. struttura/output desiderato;
4. vincoli;
5. Prompt;
6. Response/revisione;
7. export.

Se durante la configurazione emerge che il progetto corrisponde chiaramente a una famiglia esistente, Diez può suggerire il passaggio di Tipo libro senza applicarlo automaticamente.

---

# 13. Condivisione delle interfacce

## Componenti visuali condivisi

Coloring + Raccolta immagini + Libro illustrato:

- quantity/positions;
- soggetti;
- ambienti;
- scene;
- consistent;
- references;
- preview;
- prompt;
- prompt pack;
- response image;
- vision/review.

## Componenti long-form condivisi

Romanzo + Saggio/Manuale + parte testuale Libro illustrato:

- outline tree;
- editor capitoli/sezioni;
- status contenuti;
- prompt per unità;
- response text;
- diff/versioni;
- editable master;
- consistency.

## Componenti dataset condivisi

Word Search + Cruciverba + Quiz + Catalogo dati:

- import tabellare;
- mapping;
- filtri;
- tabella editabile;
- generazione batch;
- validazione;
- sostituzioni chirurgiche;
- export profili.

Condividere componenti non significa uniformare le domande: il **workflow resta specifico della famiglia**.

---

# 14. Prossima fase di design

Prima di cambiare il Prompt Compiler:

1. rivedere con l'utente Coloring 1–4 sulla build fisica;
2. congelare i nomi e la sequenza delle decisioni;
3. applicare lo stesso criterio metodologico agli altri percorsi;
4. definire le chiavi canoniche e gli stati `Defined / Propose / Derive / Later`;
5. soltanto dopo collegare il compilatore compositivo.

Questo evita di modificare ripetutamente il Prompt Engine durante l'assestamento della UX.
