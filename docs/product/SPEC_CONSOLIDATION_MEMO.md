# Memo — regola di consolidamento delle specifiche

Status: **REGOLA DI GOVERNANCE DEL PROGETTO**

Questa regola vale per Diez Publishing Studio e prevale sull'uso informale di termini come “finito”, “stabile”, “definitivo”, “consolidato” o “base corrente”.

## Principio fondamentale

**Solo ciò che è stato installato e testato fisicamente sul PC dell'utente può essere dichiarato CONSOLIDATO nelle specifiche effettive e diventare base obbligatoria della versione da portare avanti.**

Una modifica può essere corretta nel codice, compilare, superare CI e test automatici, ma fino alla prova fisica dell'installer rimane **NON CONSOLIDATA**.

## Stati ammessi

1. **Proposta / in lavorazione** — requisito o implementazione ancora in modifica.
2. **Verificata tecnicamente** — build, CI, pianisti, test automatici e controlli statici possono essere verdi. Questo stato non equivale a validazione dell'esperienza reale.
3. **Validata fisicamente** — la build è stata installata sul PC dell'utente e il comportamento è stato provato nell'app reale, includendo dove pertinente ridimensionamento finestra, input, navigazione, file picker, salvataggio/riapertura, preview, produzione AI/Vision ed errori/click casuali da “test del pianista”.
4. **CONSOLIDATA** — solo dopo conferma esplicita della prova fisica. Da questo momento il comportamento entra nelle specifiche effettive ed è la base da preservare nelle versioni successive.

## Regole operative

- Una CI verde consente di consegnare una build da provare, non di consolidare la UX.
- I pianisti dimostrano contratti tecnici e regressioni automatiche; non sostituiscono il test fisico dell'installer.
- Le specifiche possono descrivere comportamento desiderato non ancora provato, ma devono marcarlo come **DIRETTIVA / NON CONSOLIDATO**.
- Un elemento non testato fisicamente non va usato come prova che la specifica sia definitiva.
- Dopo ogni prova fisica, si aggiornano le specifiche distinguendo chiaramente cosa è stato **CONSOLIDATO**, cosa va corretto e cosa resta da testare.
- Le versioni successive devono preservare il comportamento consolidato salvo decisione esplicita dell'utente di cambiarlo.
- Se una nuova implementazione contraddice un comportamento già consolidato, la regressione va corretta prima di usare la nuova build come base.
- Windows/macOS/Linux CI resta necessaria per le promesse cross-platform, ma il consolidamento UX segue comunque la regola della prova fisica richiesta dall'utente.

## Applicazione ai libri con immagini

Il percorso Coloring Book / Raccolta immagini / Libro illustrato, inclusi stepper quantità, Consistent, Scene, partecipanti, Prompt, preview, Vision e finalizzazione, resta **NON CONSOLIDATO** finché la relativa build installata non viene provata fisicamente e approvata dall'utente.

Quando una prova viene approvata, il relativo contratto prodotto deve registrare la voce come **CONSOLIDATA DA TEST FISICO** e quella versione diventa il riferimento per gli interventi successivi.
