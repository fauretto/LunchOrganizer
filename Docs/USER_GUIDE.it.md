# LunchOrganizer — Guida utente

Questa guida è destinata alle persone che usano LunchOrganizer tutti i giorni. Non presuppone alcuna conoscenza tecnica.

L'applicazione si apre in francese per impostazione predefinita ed è disponibile in francese e in inglese. Ogni pulsante citato qui sotto è riportato come **francese *(inglese)*** seguito dal significato in italiano, così da poterlo riconoscere sullo schermo in entrambe le lingue — il selettore **FR / EN** in alto a destra la cambia in qualsiasi momento, e la scelta viene ricordata su quel computer.

- **Parte 1 — Prenotare il pranzo** è per tutti.
- **Parte 2 — Amministrazione** è per chi gestisce menu, prezzi e dipendenti.

> L'installazione o lo spostamento dell'applicazione sono trattati in [`INSTALLATION.md`](INSTALLATION.md), non qui.
>
> *Une version française de ce guide est disponible : [`USER_GUIDE.fr.md`](USER_GUIDE.fr.md). Versione originale in inglese: [`USER_GUIDE.md`](USER_GUIDE.md).*

---

## L'unica regola che conta davvero

> ### Il pranzo di oggi può essere prenotato o modificato fino alle **09:00**.
>
> Dopo le 09:00 la colonna di oggi si blocca e non può più essere modificata — la cucina ha già ricevuto l'elenco. Domani e il resto della settimana restano aperti come sempre.

Tutto il resto di questa guida è dettaglio. Questa è la parte che vale la pena ricordare.

---

# Parte 1 — Prenotare il pranzo

## Aprire l'applicazione

Digitare in un qualsiasi browser l'indirizzo fornito dall'amministratore. **Non** serve una password e **non** occorre installare nulla. Funziona anche da telefono o tablet — al di sotto di una certa larghezza la griglia settimanale si impila mostrando un giorno dopo l'altro.

## Passo 1 — Scegliere la settimana

In alto ci sono due pulsanti grandi:

| Pulsante | Mostra |
|---|---|
| **Cette semaine** *(This week)* — *«Questa settimana»* | Il lunedì–venerdì corrente |
| **Semaine prochaine** *(Next week)* — *«La settimana prossima»* | Il lunedì–venerdì successivo |

L'intestazione scrive la settimana per esteso — *"Semaine du lundi 17 août"* — invece di indicare un numero di settimana. Una piccola freccia accanto ai pulsanti permette di raggiungere settimane più lontane. Sulla settimana corrente non c'è la freccia "precedente", perché le settimane passate non possono essere prenotate.

Compaiono solo i giorni da lunedì a venerdì. Non ci sono pranzi nel fine settimana.

## Passo 2 — Digitare il proprio nome

Nel campo **Votre nom** *(Your name)* — *«Il tuo nome»* — iniziare a scrivere. Dopo **due lettere** compare un elenco di nomi corrispondenti; selezionare il proprio con il mouse oppure con le frecce e Invio.

Gli accenti non contano — digitando `chloe` si trova *Chloé Bernard* — e anche le maiuscole vengono ignorate.

**Se il proprio nome non è nell'elenco**, l'applicazione propone *"Register «your name» as a new employee"* (registra «il tuo nome» come nuovo dipendente). Basta confermare una volta e si viene aggiunti — senza bisogno di un amministratore. Se esistono già nomi simili vengono mostrati per primi: si raccomanda quindi di controllare prima di confermare, perché **Jean Dupont** e **Jean Dupond** sono due persone diverse e la cucina cucinerà per entrambe.

## Passo 3 — Leggere lo stato di oggi

Non appena l'applicazione sa chi siete, un banner riassume la giornata, per esempio:

> **Today (Mon 17 Aug): Menu 2 booked** · editable for another 1 h 12 min
>
> *(Oggi (lun 17 ago): Menu 2 prenotato · modificabile ancora per 1 h 12 min)*

oppure

> **No lunch booked for today** · you can still book for another 1 h 12 min
>
> *(Nessun pranzo prenotato per oggi · puoi ancora prenotare per 1 h 12 min)*

Una volta passate le 09:00 il banner lo segnala, e la colonna di oggi risulta bloccata.

## Passo 4 — Scegliere i menu

La griglia è il foglio cartaceo trasportato sullo schermo: **una colonna per giorno, una riga per menu.** Ogni giorno ha i propri menu, le proprie descrizioni e il proprio prezzo — il *Menu 2* di lunedì e il *Menu 2* di martedì sono piatti diversi.

- Fare clic su una casella per prenotare quel menu in quel giorno.
- Fare di nuovo clic sulla casella selezionata per eliminare il pranzo di quel giorno.
- **Un solo pranzo a persona al giorno.** Scegliere un secondo menu nello stesso giorno sostituisce il primo, non lo aggiunge.
- Le caselle in grigio non sono utilizzabili. Passandoci sopra con il mouse viene spiegato il motivo — il giorno è passato, oppure sono passate le 09:00.
- Un giorno per cui non è ancora stato pubblicato alcun menu semplicemente non mostra nulla da scegliere.

### Prenotare tutta la settimana in una volta

Sotto la griglia si sceglie un numero di menu e si preme **Appliquer le menu N à tous les jours ouverts** *(Apply Menu N to every open day)* — *«Applica il menu N a tutti i giorni aperti»*. Il comando riempie ogni giorno ancora aperto e poi riporta esattamente ciò che ha fatto: quali giorni ha applicato e quali ha saltato e perché (giorno già passato, orario limite superato, oppure quel menu non è previsto quel giorno). Nulla viene nascosto.

## Passo 5 — Salvare

Nulla è prenotato finché non si preme **Réserver mes repas** *(Book my lunches)* — *«Prenota i miei pasti»*. Una sola pressione salva l'intera settimana.

Compare una conferma verde e la griglia viene ricaricata dal database — così ciò che si vede dopo il salvataggio è sempre quanto è realmente memorizzato, non semplicemente quanto si è cliccato.

## Modificare o annullare

Si può tornare in qualsiasi momento, selezionare il proprio nome e modificare le caselle come prima. Le regole sono le stesse: oggi fino alle 09:00, i giorni successivi liberamente, i giorni passati mai.

**Per annullare un pranzo**, fare clic sulla casella selezionata per deselezionarla, poi salvare. Se non si salva, non cambia nulla.

## Stampa

Sia la pagina di prenotazione sia il report dell'amministrazione si stampano in modo pulito — basta usare il normale comando di stampa del browser. La navigazione e i pulsanti vengono rimossi automaticamente, e l'intestazione riporta la settimana e la data di stampa.

---

# Parte 2 — Amministrazione

## Accesso

Aprire **Administration** nella navigazione in alto. Gli account amministratore sono contenuti in un piccolo file (`config/admin-users.json`) che un amministratore può modificare con Notepad++; non esiste una schermata di creazione account. Le sessioni durano 8 ore.

Dopo cinque tentativi falliti in un minuto il login si sospende brevemente. Basta attendere un momento e riprovare.

L'area di amministrazione ha tre schede.

## Scheda Menu e prezzi

È la scheda usata più spesso, di solito una volta a settimana.

**Menu.** Scegliere una settimana, poi per ogni giorno aggiungere i menu con **Ajouter un menu** *(Add a menu)* — *«Aggiungi un menu»* — e scrivere una descrizione, ad esempio *"Poulet au curry, riz basmati"*. I menu sono numerati automaticamente a partire da 1. Un menu appartiene a **una data specifica**: impostare il lunedì non dice nulla sul martedì. **Copier cette description sur toute la semaine** *(Copy this description across the whole week)* — *«Copia questa descrizione su tutta la settimana»* — evita di riscrivere quando lo stesso piatto viene proposto per più giorni. I menu possono anche essere importati in blocco da un documento Word con **Importer des menus** *(Import menus)* — *«Importa menu»*; il nuovo formato di documento mensile — celle giorno senza riga di intestazione, ciascuna con il proprio giorno e anno — è supportato, ma l'importazione viene rifiutata se l'anno indicato nel documento non corrisponde a quello selezionato nella finestra di importazione.

**Prezzi.** Ogni giorno ha un unico prezzo, condiviso da tutti i menu di quel giorno. Lo si imposta giorno per giorno, oppure si usa **Appliquer ce prix à toute la semaine** *(Apply this price to the whole week)* — *«Applica questo prezzo a tutta la settimana»*. I nuovi giorni partono dal prezzo predefinito indicato in `config/app.json` (attualmente **CHF 12.50**). Un menu può anche avere un proprio prezzo, impostato in un piccolo campo accanto alla descrizione — lasciarlo vuoto per usare il prezzo del giorno — e questo prezzo può anche arrivare automaticamente da un documento importato quando il documento ne indica uno.

> **Modificare un prezzo non riscrive mai il passato.** Ogni prenotazione memorizza il prezzo in vigore nel momento in cui è stata effettuata. Se il prezzo di martedì cambia dopo che dieci persone hanno già prenotato, quelle dieci mantengono il prezzo al quale hanno prenotato e il report resta corretto. I prezzi valgono sempre e solo da lì in avanti.

**L'eliminazione di un menu** viene rifiutata finché qualcuno lo ha prenotato — verrà indicato quante prenotazioni esistono. Occorre prima rimuovere o spostare quelle prenotazioni. È una scelta deliberata: rende impossibile lasciare qualcuno con un pranzo che non esiste più.

## Scheda Dipendenti

Un elenco consultabile di tutte le persone registrate. Si possono aggiungere persone manualmente e modificare nome o indirizzo e-mail direttamente nella riga.

**Eliminare o disattivare.** Chi non ha mai prenotato può essere eliminato del tutto. Chi ha uno storico di prenotazioni **non** può esserlo: eliminarlo corromperebbe i report passati. L'applicazione propone invece **Désactiver** *(Deactivate)* — *«Disattiva»*: la persona smette di comparire nel completamento automatico della pagina di prenotazione, mentre il suo storico resta intatto per i report. È la scelta giusta per chi ha lasciato l'azienda.

## Scheda Report

Scegliere un singolo dipendente oppure *Tous les employés* *(All employees)* — *«Tutti i dipendenti»* —, impostare una data di inizio e una di fine, e la tabella elencherà ogni prenotazione — **Data · Giorno · Menu · Descrizione · Prezzo** — con il **totale del periodo** in una scheda riepilogativa. È questo lo strumento da usare per fatturare o riconciliare.

**Exporter en CSV** *(Export to CSV)* — *«Esporta in CSV»* — scarica la stessa tabella per Excel. Il file è in UTF-8 con byte-order mark, così i nomi accentati si aprono correttamente in Excel senza alcuna procedura di importazione.

---

# L'e-mail quotidiana

Ogni giorno lavorativo alle **09:01**, un minuto dopo l'orario limite, LunchOrganizer invia alla cucina un riepilogo delle prenotazioni della giornata.

L'e-mail è raggruppata per menu, ciascuno con la propria descrizione e l'elenco alfabetico delle persone che lo hanno scelto, e un totale in cima. I menu che nessuno ha scelto vengono omessi. Viene inviata in francese per impostazione predefinita, indipendentemente dalla lingua che si usa personalmente sul sito.

**Se nessuno ha prenotato, non viene inviato nulla.** La cucina non riceve mai un elenco vuoto.

Alcune cose che vale la pena sapere:

- L'e-mail viene inviata da un piccolo programma pianificato, **non** dal sito web. Parte comunque anche se il sito è stato riavviato o non è in esecuzione.
- Il riepilogo **non può mai essere inviato due volte** per lo stesso giorno, nemmeno se due copie dello scheduler vengono eseguite contemporaneamente. Vince la prima che si aggiudica la giornata.
- L'invio alle 09:01 anziché alle 09:00 è voluto: garantisce che una prenotazione salvata alle 08:59:59 sia inclusa nell'e-mail.

---

# Domande frequenti

**Ho prenotato ma non compaio nell'e-mail.**
Quasi sempre la prenotazione è stata salvata dopo le 09:00, quindi è valsa per un giorno successivo anziché per oggi. Aprire la settimana e controllare quale casella è selezionata.

**Qualcuno ha registrato il mio nome due volte, con una piccola differenza.**
Un amministratore può disattivare quello sbagliato dalla scheda Dipendenti. L'applicazione blocca già i duplicati esatti — comprese le differenze di maiuscole o accenti, per cui `ALICE MARTIN` non può essere aggiunto accanto ad `Alice Martin` — ma non può sapere che *Jean Dupond* doveva essere *Jean Dupont*.

**Ho dimenticato di prenotare e sono le 09:30.**
L'applicazione non può fare nulla — l'elenco per la cucina è già partito. Occorre rivolgersi direttamente a loro.

**Posso prenotare per un collega?**
Sì. Basta digitare il suo nome al posto del proprio. Sulla pagina di prenotazione non c'è alcuna password. Si basa sulla fiducia, esattamente come il foglio cartaceo che sostituisce.

**Posso prenotare con diverse settimane di anticipo?**
Sì, fin dove i menu sono stati pubblicati. I giorni per cui non ci sono ancora menu non possono essere prenotati.

**La pagina dice che si sta riconnettendo.**
Il sito mantiene una connessione attiva con il server. Se la rete ha un'interruzione momentanea si riconnette da solo e, se non ci riesce, propone un pulsante per riprovare. Le prenotazioni già salvate non ne risentono mai — si perderebbero solo i clic non ancora salvati.

**Ho cambiato lingua e le mie prenotazioni sono sparite.**
Non è così. Il cambio di lingua ricarica la pagina; basta reinserire il proprio nome e la griglia torna esattamente com'era.

---

*Le domande a cui questa guida non risponde si trovano probabilmente in [`DEBUGGING.md`](DEBUGGING.md) — versione italiana: [`DEBUGGING.it.md`](DEBUGGING.it.md) — per l'esecuzione dell'applicazione, oppure in [`INSTALLATION.md`](INSTALLATION.md) per l'installazione e la configurazione.*
