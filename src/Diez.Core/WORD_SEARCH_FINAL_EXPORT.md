# Word Search — export finale

Il Word Search finale deve essere esportabile **sia XLSX sia CSV** dopo lo stesso gate di finalizzazione del libro.

## Formato Self Publishing Titans

Il file `wordsearch_sample(1).csv` fornito come riferimento di prodotto definisce il profilo di handoff da rispettare.

Contratto funzionale:

- una colonna per puzzle;
- intestazioni in forma `puzzle 1`, `puzzle 2`, `puzzle 3`, ...;
- una riga per posizione della parola;
- nessuna riga tecnica Diez (`ID`, `Tema`, `Stato`, `Origine`, `Note`, ecc.) nel file finale;
- le celle mancanti rimangono vuote;
- l'ordine dei puzzle segue l'ordine editoriale del libro;
- CSV in UTF-8 con BOM, separatore virgola `,` e line ending LF;
- le celle semplici non vengono quotate; le virgolette CSV vengono aggiunte soltanto quando necessarie per valori contenenti virgole, virgolette o ritorni a capo;
- XLSX e CSV devono rappresentare la stessa matrice puzzle/parole.

Il sample contiene anche tre colonne finali vuote e righe vuote di padding. Queste sono considerate **padding del file di esempio**, non puzzle o parole reali: Diez non deve inventare elementi per riprodurre il padding. Se in futuro Self Publishing Titans dimostra di richiedere un padding fisso per l'import, quel padding verrà aggiunto come profilo di compatibilità separato senza alterare il modello libro.

## Gate finale

XLSX e CSV finali devono essere bloccati finché non sono soddisfatti tutti i controlli Word Search già canonici:

- numero puzzle esatto;
- numero parole esatto per puzzle;
- unicità globale quando `NoDuplicates` è attivo;
- nessuna parola `KDPSAFE=NO` usata;
- tutti i puzzle approvati.

Gli export di lavoro/database possono continuare ad avere formati più ricchi e contenere metadata; questo contratto riguarda il **handoff finale** destinato al tool di impaginazione/generazione.
