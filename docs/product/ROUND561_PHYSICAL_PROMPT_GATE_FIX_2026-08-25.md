# Round 5.6.1 — Prompt gate fisico dopo Libreria Temi

Status: **TECHNICALLY_VERIFIED / DA VERIFICARE FISICAMENTE / NON CONSOLIDATO**

Data: 2026-08-25

Branch: `spike/uno-platform-ui`

## Riscontro fisico utente

Nell'installer Round 5.6 tecnicamente verificato (Run #47, Source `d76b8acb9634168a50dc4a9b895ec49d8a776014`), il percorso visuale può arrivare alla fase Prompt mostrando:

`Prompt non disponibile: ...`

Il problema è emerso durante la prova reale della nuova Libreria Temi.

La Round 5.6 resta quindi **NON CONSOLIDATA**.

## Diagnosi

Il Core Round 5.6 sapeva compilare correttamente una proposta Tema già accettata, ma la Uno non aveva un gate esplicito fra Definizione e Prompt.

`Salva e continua → Prompt` poteva quindi portare l'utente nella fase successiva anche con piano soggetti ancora irrisolto. La fase Prompt provava poi a compilare e trasformava l'eccezione semantica nel generico `Prompt non disponibile`.

Il regression precedente verificava `AcceptProposal → BuildPromptPack`, ma non riproduceva il round-trip reale della UI dopo refresh e nuovo salvataggio della Definizione.

## Fix implementato

Round 5.6.1 introduce una readiness semantica esplicita.

Sono considerati risolti:

1. proposta Tema accettata e congelata in un `MultiSubjectProfile` completo;
2. Scene/Soggetti strutturati completi e concreti;
3. percorso legacy/generico con soggetto realmente concreto, per non rompere i progetti precedenti.

Non sono risolti:

- proposta Tema preparata ma non accettata;
- tema aggregato senza soggetti atomici congelati;
- placeholder o piano incompleto.

`Salva e continua → Prompt` ora resta nella Definizione se il piano non è risolto e indica la prossima azione. Se esiste una proposta pronta, il messaggio chiede esplicitamente di controllarla e premere `Accetta e congela i soggetti`.

La fase Prompt contiene inoltre un secondo gate difensivo: se viene raggiunta da cronologia/navigazione con stato irrisolto, non tenta il renderer e mostra in italiano cosa manca.

## Regression aggiunta

`tests/Diez.VisualSemanticRegression/Round561PromptGateRegression.cs`

Il test riproduce:

`Save setup → Proponi Tema → Accetta → rileggi setup dopo refresh → salva di nuovo la Definizione → BuildPromptPack`.

Verifica che:

- prima dell'accettazione il piano non risulti risolto;
- dopo l'accettazione risulti risolto;
- un successivo salvataggio della Definizione non perda il `MultiSubjectProfile` congelato;
- `BuildPromptPack` produca esattamente il numero richiesto di prompt atomici;
- l'aggregato `3 soggetti di Halloween` non diventi il PRIMARY SUBJECT del renderer dopo il freeze;
- il percorso legacy con soggetto concreto resti compatibile.

Il regression è passato sia nel run tecnico #48 sia nella candidata pulita #49.

## Candidata Windows finale

Source SHA: `26a3d172c16eee6c2e548cfb58730c208cb91a56`

Workflow: `Uno Windows Consolidation Candidate`

Run: **#49**

Run ID: `32831362004`

Candidate status: **TECHNICALLY_VERIFIED**

Esiti:

- Visual book gate: `success`;
- semantic regression, incluso Round 5.6.1: `success`;
- restore: `success`;
- publish: `success`;
- verify executable: `success`;
- package: `success`;
- smoke install/launch/uninstall: `success`;
- artifact upload: `success`.

Artifact ID: `9556971449`

Artifact: `DiezPublishingStudio-UnoPreview-Windows-x64`

Artifact ZIP bytes: `94472174`

Artifact ZIP SHA-256: `5f1b029c2bbce82a9629a78f13b6b2b6596313b0c9ec7441a1a9acc32e79fc8c`

Setup bytes: `95001181`

Setup SHA-256: `51bd7eb179931aff8e6f2d6e159cbbc3f91e8573efaa12cb2bc9bc8f0aaa14ef`

Gli hash sono stati verificati anche sul download locale dell'artifact.

## Scope

Questo fix è **Round 5.6.1** e non implementa la nuova disposizione UI Round 5.7. La Round 5.7 resta una decisione/spec separata.

## Verifica fisica richiesta

Ripetere nell'app installata:

`Numero immagini → Tema → Proponi soggetti → controlla/modifica → Accetta e congela → Salva e continua → Prompt`.

Atteso:

- se manca l'accettazione, Diez resta in Definizione con un messaggio esplicito;
- dopo l'accettazione, il Prompt viene compilato;
- il Prompt Pack resta disponibile con una Work Unit atomica per soggetto congelato.

Round 5.6.1 resta **NON CONSOLIDATO** fino a questa verifica fisica.