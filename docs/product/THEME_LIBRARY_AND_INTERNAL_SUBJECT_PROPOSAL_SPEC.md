# Libreria Temi + proposta soggetti interna Diez

Status: **SPECIFICA APPROVATA / DA IMPLEMENTARE**

Data: 2026-08-24

Branch: `spike/uno-platform-ui`

## Decisione prodotto

Il flusso del Piano soggetti non deve esporre all'utente Prompt intermedi, JSON, Candidate tecniche, versioni del planner o operazioni di copy/paste.

Principio:

**L'utente sceglie il tema e decide i soggetti; Diez gestisce analisi, struttura canonica e formato tecnico.**

Flusso visibile:

**Tema → Proposta soggetti Diez → verifica/modifica utente → Accetta → Prompt → Prompt Pack**

Nessun JSON è visibile o richiesto all'utente.

## 1. Libreria temi

Diez introduce una libreria temi riutilizzabile, analoga concettualmente alla libreria stili.

La UI mostra un selettore `Tema` con temi generali integrati e voci Custom archiviate dall'utente.

Seed iniziale indicativo:

- Animali della giungla
- Halloween
- Natale / Christmas
- Dinosauri
- Spazio
- Fattoria
- Oceano / mare
- Animali domestici
- Veicoli
- Fiabe / fantasy
- Fiori / botanica
- Inverno
- Primavera
- Pasqua
- San Valentino
- Custom

L'elenco può crescere senza modificare il Prompt Compiler.

## 2. Un tema non è una lista fissa di tre soggetti

La libreria può contenere un vocabolario editoriale curato di soggetti collegati al tema, ma è vietato implementare una regola rigida del tipo:

`Halloween => zucca, fantasma, gatto`.

Ogni `ThemeDefinition` contiene invece un pool di concetti possibili e metadati semantici.

Esempio concettuale:

- ThemeId stabile
- nome visibile italiano
- canonical theme
- alias/tag
- famiglie libro compatibili
- fasce pubblico compatibili
- pool di `ThemeSubjectConcept`
- eventuali esclusioni/attenzioni editoriali
- provenienza: BUILTIN | USER_AI_EXPANDED | USER_AUTHORED

Ogni `ThemeSubjectConcept` contiene almeno:

- display label italiano
- canonical concept tecnico
- breve descrizione italiana
- canonical description tecnica opzionale
- tag/affinità
- livello di complessità indicativo
- compatibilità pubblico

## 3. Proposta soggetti interna

Quando l'utente seleziona un tema integrato e chiede N immagini, Diez propone N soggetti **senza chiamare un'AI esterna**.

Il resolver interno usa:

- tema selezionato;
- numero immagini;
- famiglia/tipo libro;
- pubblico;
- stile/profilo editoriale;
- vincoli DEVE FARE / NON DEVE FARE semanticamente pertinenti;
- eventuali materiali/Scene/Soggetti già espliciti;
- necessità di varietà e non duplicazione.

Output visibile: N card modificabili, ciascuna con nome e descrizione in italiano.

Output interno: stato canonico completo con identificatori e concetti provider-facing.

La stessa configurazione semanticamente invariata deve produrre una proposta stabile/riproducibile, salvo richiesta esplicita `Rigenera proposta`.

## 4. UX proposta

Nel blocco `Piano soggetti`:

1. `Tema` — ComboBox/libreria
2. `Numero immagini`
3. pulsante `Proponi soggetti`
4. elenco card proposte
5. per ogni card: `Nome` + `Descrizione`, modificabili
6. azione `Rigenera proposta`
7. azione `Accetta e congela i soggetti`

L'utente non vede:

- Prompt planner;
- JSON;
- Work Unit TEXT planner;
- Candidate/versioni tecniche;
- canonical_concept;
- schema del trasporto.

## 5. Tema Custom

Se l'utente sceglie `Custom` compare:

- campo `Nome tema`;
- decisione `Usa solo in questo progetto`;
- decisione `Salva anche nella libreria temi`;
- azione `Espandi tema con AI` quando almeno un provider API è configurato.

Il comportamento richiesto è analogo allo stile Custom: nessun salvataggio cross-project implicito.

### 5.1 Espansione AI Custom

Per un tema Custom, Diez può usare una chiamata AI **interna e automatica** per ricercare/espandere parole e soggetti collegati.

L'utente non copia né incolla nulla.

Pipeline:

**nome tema utente → semantic theme expansion request → provider API → structured result interno → validazione Diez → anteprima parole/soggetti → conferma utente → libreria temi**

Il provider può essere OpenAI, Gemini o altro executor realmente configurato.

La risposta strutturata è un dettaglio di protocollo interno. Se il provider supporta Structured Outputs/schema nativo, Diez deve preferirlo.

L'espansione deve produrre un pool abbastanza ampio da essere riutilizzabile, non soltanto i N soggetti del progetto corrente.

Prima dell'aggiunta alla libreria l'utente vede e può modificare/rimuovere le voci trovate.

## 6. Nessun fallback copy/paste

Se non è configurata una API diretta:

- i temi BUILTIN restano pienamente operativi localmente;
- un tema Custom può essere nominato e usato con contenuti inseriti manualmente;
- `Espandi tema con AI` mostra `Richiede una API configurata`;
- Diez NON deve riproporre il vecchio percorso copy/paste del prompt planner.

Il collegamento API deve quindi diventare una capability opzionale, non un requisito per i temi standard.

## 7. Stato canonico e persistenza

Le scelte visibili dell'utente persistono nel progetto.

La libreria cross-project è separata dal file `.diez` e usa identità proprie.

Un progetto conserva almeno:

- ThemeId o snapshot del tema scelto;
- origine tema;
- nome tema visibile;
- soggetti accettati con nuovi SubjectId;
- eventuali modifiche utente;
- snapshot semantico usato dal Prompt Compiler.

Un aggiornamento successivo della libreria non deve cambiare silenziosamente un progetto già congelato.

## 8. Importa impostazioni da altro progetto

La funzione di import delle impostazioni può importare:

- tema scelto dall'utente;
- eventuale nome tema Custom;
- soggetti/testi esplicitamente modificati o decisi dall'utente.

Non deve importare:

- cache di espansione AI;
- cronologia delle chiamate;
- risultati tecnici non approvati;
- chiavi API;
- identificatori provider;
- stato di trasporto.

## 9. Sicurezza API

Quando verrà collegato un provider API:

- la chiave segreta non deve essere salvata nel progetto `.diez`;
- non deve essere salvata nel repository;
- non deve apparire in log, Prompt Pack o Response Pack;
- deve essere conservata nello storage sicuro del sistema operativo / secret store locale;
- Diez deve supportare verifica connessione, revoca e sostituzione chiave.

## 10. Relazione con Round 5.3–5.5

Questa specifica sostituisce come UX ordinaria il planner TEXT manuale introdotto nelle Round 5.4–5.5.

Il gate semantico Round 5.3 resta valido come rete di sicurezza: un tema aggregato non risolto non può arrivare al renderer.

Il vecchio planner TEXT può restare temporaneamente solo come infrastruttura interna di migrazione/test, ma non deve essere il percorso utente finale.

## 11. Criteri di regressione futuri

Fallimento se:

1. un tema BUILTIN richiede una chiamata AI per proporre soggetti;
2. l'utente deve vedere o scrivere JSON;
3. l'utente deve copiare/incollare un Prompt planner;
4. un tema produce sempre una tripletta hardcoded indipendentemente da pubblico/stile/numero;
5. un Custom viene aggiunto alla libreria senza decisione utente;
6. l'espansione Custom modifica la libreria prima della revisione utente;
7. il Prompt Compiler usa la label UI al posto dello stato canonico;
8. la chiave API entra nel `.diez`, nel repository o nei pacchetti AI;
9. un aggiornamento della libreria modifica retroattivamente un progetto congelato.
