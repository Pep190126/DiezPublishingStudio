# Round 5.6.1 — Prompt gate fisico dopo Libreria Temi

Status: **RISCONTRO FISICO / FIX DA IMPLEMENTARE / NON CONSOLIDATO**

Data: 2026-08-25

Branch: `spike/uno-platform-ui`

## Riscontro fisico utente

Nell'installer Round 5.6 tecnicamente verificato (Run #47, Source `d76b8acb9634168a50dc4a9b895ec49d8a776014`), il percorso visuale può arrivare alla fase Prompt mostrando:

`Prompt non disponibile: ...`

Il problema è emerso durante la prova reale della nuova Libreria Temi.

La Round 5.6 resta quindi **NON CONSOLIDATA**.

## Difetto di UX / gate

La UI consente di usare `Salva e continua → Prompt` anche quando la risoluzione concreta dei soggetti non è ancora completa. La fase Prompt prova poi a compilare e trasforma l'eccezione semantica in un generico `Prompt non disponibile`.

Questo è un handoff sbagliato fra Definizione e Prompt: una decisione mancante deve essere spiegata e risolta in Definizione, non scoperta soltanto dal renderer/compiler nella fase successiva.

## Requisito Round 5.6.1

Prima di entrare in Prompt, Diez deve determinare se il piano soggetti è realmente risolto.

Sono considerati risolti almeno questi casi compatibili:

1. proposta Tema accettata e congelata in un `MultiSubjectProfile` completo;
2. Scene/Soggetti strutturati completi e concreti;
3. percorso legacy/generico con soggetto singolo realmente concreto, per non rompere i progetti precedenti.

Non sono risolti:

- proposta Tema preparata ma non accettata;
- tema aggregato senza soggetti atomici congelati;
- placeholder o piano incompleto.

Se non risolto, `Salva e continua → Prompt` deve restare nella Definizione e mostrare una prossima azione precisa, per esempio:

`Controlla la proposta e premi Accetta e congela i soggetti prima di creare il Prompt.`

La fase Prompt deve inoltre avere una seconda protezione: se viene raggiunta con stato irrisolto, deve mostrare il gate editoriale in italiano invece di tentare la compilazione e mostrare soltanto l'eccezione tecnica.

## Regression obbligatoria

Il test deve riprodurre il round-trip reale della Uno:

`Save setup → Proponi Tema → Accetta → rileggi setup dopo refresh → salva di nuovo la Definizione → BuildPromptPack`.

Il test deve verificare che:

- prima dell'accettazione il piano non risulti risolto;
- dopo l'accettazione risulti risolto;
- un successivo salvataggio della Definizione non perda il `MultiSubjectProfile` congelato;
- `BuildPromptPack` produca esattamente il numero richiesto di prompt atomici;
- il percorso legacy con soggetto concreto non venga erroneamente bloccato dal nuovo gate;
- un aggregato non accettato continui invece a essere bloccato.

## Scope

Questo fix è **Round 5.6.1** e non implementa ancora la nuova disposizione UI Round 5.7. La Round 5.7 resta una decisione/spec separata.

La correzione 5.6.1 deve essere verificata con nuova candidata Windows e prova fisica prima di consolidare.