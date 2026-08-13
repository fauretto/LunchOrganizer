# LunchOrganizer — Guida al debug

Come eseguire, debuggare e ispezionare LunchOrganizer su **questo portatile**. Tutto quanto segue è stato eseguito su questa macchina il 12 agosto 2026; i comandi sono riportati esattamente come sono stati lanciati, non come dovrebbero funzionare in teoria.

Per l'installazione su un'altra macchina, vedere [`INSTALLATION.md`](INSTALLATION.md). Per capire come si presenta l'applicazione a chi la usa, vedere [`USER_GUIDE.md`](USER_GUIDE.md) (francese: [`USER_GUIDE.fr.md`](USER_GUIDE.fr.md)). Per ciò che resta da testare a mano, vedere [`TEST_CHECKLIST.md`](TEST_CHECKLIST.md).

---

## 1. Cosa è già installato qui

| Componente | Stato su questa macchina |
|---|---|
| .NET SDK | **10.0.302** (e 9.0.316). `global.json` fissa la banda `10.0.3xx`. |
| PostgreSQL | **16.14**, servizio Windows `postgresql-x64-16`, in esecuzione. |
| `psql.exe` | **`D:\PostgreSQL\bin\psql.exe`** — **non presente nel `PATH`**. Usare il percorso completo. |
| Directory dei dati | `D:\Ismeca\NativeNET\PostgreSQL\data_16.3` |
| Database | `lunchorganizer`, creato dall'applicazione stessa al primo avvio. |
| Credenziali | `config/database.local.json` — escluso da git, contiene la password reale. `config/database.json` mantiene il segnaposto `changeme`. |

Non serve installare altro per fare debug.

---

## 2. Primo avvio

```powershell
cd C:\Projects_Git\Data\GitPerso\LunchOrganizer
dotnet tool restore      # restores the local dotnet-ef tool (.config/dotnet-tools.json)
dotnet build LunchOrganizer.sln
dotnet run --project src/LunchOrganizer.Web
```

Il sito è in ascolto su **http://localhost:5226** (e su **https://localhost:7047** con il profilo `https`). Entrambi provengono da `src/LunchOrganizer.Web/Properties/launchSettings.json`.

- **Pagina di prenotazione:** `/`
- **Amministrazione:** `/admin` → login **`massimo` / `changeme`** (da `config/admin-users.json`)
- **Lingua:** francese per impostazione predefinita; il selettore `FR · EN` si trova nell'intestazione.

Al primissimo avvio l'applicazione crea il database, applica la migrazione, installa `citext` e `unaccent` e — poiché `config/database.local.json` imposta `"Seed": true` — inserisce i dati dimostrativi. Ci si aspetta una riga di log come:

```
Database bootstrap complete: database created, 1 migration(s) applied, seeding ran.
```

Per scegliere una porta diversa senza modificare `launchSettings.json`:

```powershell
dotnet run --project src/LunchOrganizer.Web --no-launch-profile --urls http://localhost:5310
```

---

## 3. Debug in un IDE

### Visual Studio / Rider

Aprire `LunchOrganizer.sln`, impostare **LunchOrganizer.Web** come progetto di avvio, premere **F5**. I profili di avvio `http` e `https` sono già definiti. I breakpoint in componenti, ViewModel, servizi e repository si attivano normalmente — si tratta di Blazor **Server**, quindi tutto quel codice gira nel processo .NET, non nel browser.

Per debuggare invece il mailer, impostare **LunchOrganizer.Mailer** come progetto di avvio e passargli gli argomenti (vedere §6).

### VS Code

Usare la configurazione di avvio generata dal C# Dev Kit per `LunchOrganizer.Web`, oppure collegarsi (attach) a un processo `dotnet` in esecuzione.

### Ciò che il browser *non* sta facendo

Non c'è WebAssembly né codice C# lato client. Il browser mantiene un circuito SignalR; gli eventi dell'interfaccia viaggiano fino al server, vengono eseguiti lì e il diff del DOM risultante torna indietro. Quindi:

- **Breakpoint C#** → nell'IDE.
- **CSS, layout, anelli di focus, anteprima di stampa** → strumenti di sviluppo del browser.
- **Un pulsante che non risponde** è di solito un problema di *render mode*, non di logica — vedere §9.

---

## 4. Ispezionare il database

`psql` non è nel `PATH`, quindi occorre usare il percorso completo. La password si trova in `config/database.local.json`.

```powershell
$psql = 'D:\PostgreSQL\bin\psql.exe'
$env:PGPASSWORD = 'P0stgr3sP0stgr3s'

# tables
& $psql -h localhost -U postgres -d lunchorganizer -c "\dt"

# what is in there
& $psql -h localhost -U postgres -d lunchorganizer -c "select count(*) from bookings;"

# today's bookings, readable
& $psql -h localhost -U postgres -d lunchorganizer -c "
  select e.full_name, m.menu_number, b.price_snapshot
  from bookings b
  join employees e on e.id = b.employee_id
  join menus m on m.id = b.menu_id
  where b.booking_date = current_date
  order by m.menu_number, e.full_name;"

$env:PGPASSWORD = $null
```

Una sessione interattiva:

```powershell
$env:PGPASSWORD='P0stgr3sP0stgr3s'; & 'D:\PostgreSQL\bin\psql.exe' -h localhost -U postgres -d lunchorganizer
```

Utili una volta dentro: `\dt` tabelle · `\d bookings` una tabella per intero · `\di` indici · `\dx` estensioni · `\q` uscita.

### Verificare che le regole di integrità siano attive

Tutte queste operazioni devono essere **rifiutate**. Se una riesce, c'è qualcosa che non va nello schema:

```sql
BEGIN;
-- Tuesday's menu on a Monday → violates the composite FK
INSERT INTO bookings (employee_id, booking_date, menu_id, price_snapshot)
SELECT (SELECT id FROM employees LIMIT 1), m.menu_date + 1, m.id, 12.50
FROM menus m ORDER BY m.id LIMIT 1;
ROLLBACK;
```

Altri casi da provare allo stesso modo: una seconda prenotazione per la stessa coppia `(employee_id, booking_date)`; un dipendente il cui nome differisce solo per maiuscole/minuscole; l'eliminazione di un dipendente che ha prenotazioni; `menu_number = 0`.

---

## 5. Reimpostare il database

Il modo più rapido per tornare a uno stato pulito e popolato con i dati di esempio — l'applicazione ricrea tutto al successivo avvio:

```powershell
$env:PGPASSWORD='P0stgr3sP0stgr3s'
& 'D:\PostgreSQL\bin\psql.exe' -h localhost -U postgres -d postgres -c "DROP DATABASE IF EXISTS lunchorganizer WITH (FORCE);"
$env:PGPASSWORD=$null
dotnet run --project src/LunchOrganizer.Web
```

`WITH (FORCE)` disconnette qualunque cosa stia ancora tenendo aperto il database — necessario se una sessione di debug è rimasta in sospeso.

Per cancellare i dati mantenendo lo schema:

```sql
TRUNCATE bookings, menus, daily_prices, employees, email_log RESTART IDENTITY CASCADE;
```

Il **seeding** è controllato dalla chiave `"Seed"` in `config/database.local.json`. È idempotente, quindi non duplicherà righe se viene eseguito di nuovo. Impostarla a `false` per lavorare su un database vuoto.

---

## 6. Debuggare l'email quotidiana — senza inviare nulla

La modalità predefinita è `PickupDirectory`, che scrive un file `.eml` invece di contattare un server di posta. Nulla può finire per sbaglio in una casella reale.

```powershell
# today
dotnet run --project src/LunchOrganizer.Mailer -- --dry-run

# a specific day
dotnet run --project src/LunchOrganizer.Mailer -- --date 2026-08-17 --dry-run

# help
dotnet run --project src/LunchOrganizer.Mailer -- --help
```

Il file `.eml` finisce in **`src\LunchOrganizer.Mailer\bin\Debug\net10.0\mail-drop\`** (il percorso in `email.json` è relativo all'eseguibile, non al repository). Basta un doppio clic per aprirlo in Outlook esattamente come lo vedrebbe un destinatario.

| Codice di uscita | Significato |
|---|---|
| `0` | Inviata |
| `1` | Fallita |
| `2` | Saltata — nessuna prenotazione quel giorno |
| `3` | Già gestita — un altro processo aveva riservato la giornata |

`--dry-run` non tocca mai `email_log`, quindi si può eseguire ripetutamente sulla stessa data. Senza `--dry-run` la giornata viene riservata e una seconda esecuzione restituisce `3` per progettazione — è la protezione contro il doppio invio, non un bug. Per rieseguire un invio reale per una data:

```sql
DELETE FROM email_log WHERE summary_date = DATE '2026-08-17';
```

Il mailer legge il **database PostgreSQL reale** — le stesse righe che mostra l'applicazione web. Non crea né migra mai il database: se PostgreSQL non è raggiungibile, oppure se il database esiste ma non contiene le tabelle di LunchOrganizer, il mailer stampa un messaggio in linguaggio comune che cita `config/database.json` ed esce con `1` senza inviare nulla. Avviare una volta l'applicazione web per creare lo schema.

### 6.1 Attivare l'email dal sito web

L'applicazione web può anche eseguire il riepilogo su un timer, cosa comoda quando si vuole osservare l'operazione nel log dell'app stessa anziché in una console separata. È **disattivata per impostazione predefinita** e dovrebbe restare tale in produzione — il meccanismo reale è l'attività pianificata (`INSTALLATION.md` §7), perché non dipende dal fatto che il sito web sia attivo.

Creare `config/email.local.json` (escluso da git) con le sole due chiavi da sovrascrivere:

```json
{
  "Email": {
    "EnableInAppScheduler": true,
    "SendTimeLocal": "14:30:00"
  }
}
```

Impostare `SendTimeLocal` un paio di minuti nel futuro, riavviare l'applicazione web e osservarne la console. Lo scheduler dovrebbe restare silenzioso fino a quel minuto e poi registrare una di queste righe:

```
Scheduled daily summary run completed: …
Scheduled daily summary run was already handled: …
Scheduled daily summary run failed: …
```

Con `Recipients` ancora vuoto si otterrà la riga `failed` con il testo *"No recipients configured … the summary was built but not sent"* — si noti **built**, che significa che la query al database è stata realmente eseguita. Eliminare poi `email.local.json`; con lo scheduler disattivato l'app registra invece all'avvio *"The in-app daily summary scheduler is disabled; DailySummaryHostedService will not run."*

> Questa è un'esecuzione **reale**, non una simulazione: riserva la giornata in `email_log`. Cancellare la riga prima di ripetere il test sulla stessa data (vedere la `DELETE` qui sopra).

---

## 7. Eseguire i test

```powershell
dotnet test LunchOrganizer.sln                                   # all 48
dotnet test --filter "FullyQualifiedName~Concurrency" -v n       # the 6 concurrency scenarios, per test
dotnet test --filter "FullyQualifiedName~Unit"                   # rules only, no database
```

I test di concorrenza richiedono un **PostgreSQL in esecuzione**. Creano ed eliminano il proprio database `lunchorganizer_test` e non toccano mai `lunchorganizer`. Leggono i dettagli di connessione da `config/database.local.json`; se non riescono a connettersi falliscono in modo rumoroso anziché essere saltati, il che è voluto — una suite di concorrenza che silenziosamente non fa nulla è peggio di nessuna suite.

Un'esecuzione sana mostra durate reali (≈1 s per il test con dieci inserimenti di menu in parallelo). Tempi dell'ordine dei millisecondi ovunque significherebbero che i test non stanno raggiungendo il database.

---

## 8. Modificare il comportamento durante il debug

Tutte queste sono modifiche a file JSON — nessuna ricompilazione. L'app web monitora i file e li ricarica (`reloadOnChange: true`); basta aggiornare la pagina.

È preferibile modificare la variante **`.local.json`**: è esclusa da git, quindi i propri esperimenti locali non finiscono mai in un commit.

| Per fare questo | Modificare | Impostazione |
|---|---|---|
| Testare il blocco "dopo le 9" alle 10:00 | `config/app.local.json` | `"BookingCutOffLocalTime": "23:00:00"` (o un orario già passato) |
| Cambiare il prezzo di un giorno senza override | `config/app.local.json` | `"DefaultLunchPrice"` |
| Forzare l'inglese indipendentemente dal browser | `config/app.local.json` | `"DefaultCulture": "en-CH"` |
| Aggiungere un account amministratore | `config/admin-users.local.json` | un'altra voce in `Users` |
| Far scattare lo scheduler dentro l'app web | `config/email.local.json` | `"EnableInAppScheduler": true`, `"SendTimeLocal"` di un minuto o due in avanti |
| Inviare tramite un server SMTP reale | `config/email.local.json` | `"Mode": "Smtp"` + host/porta/credenziali |

Le password degli amministratori possono essere scritte sia in chiaro sia come `"sha256:<hex>"`. Entrambe le forme sono accettate. Modificare `admin-users.json` **revoca una sessione attiva**, non solo i login futuri — il cookie viene rivalidato rispetto al file a ogni richiesta.

---

## 9. Risoluzione dei problemi

**Un pulsante o una casella di spunta non fa nulla.**
La pagina è stata resa come HTML statico senza circuito interattivo. I componenti Blazor hanno bisogno di `@rendermode InteractiveServer`, che non può essere applicato a `MainLayout` — il layout riceve un `RenderFragment` (`Body`) non serializzabile attraverso il confine di rendering. Va messo sui componenti foglia (`Booking.razor`, `AppHeader.razor`, `ToastHost.razor`, `ConfirmDialogHost.razor`). Verificare dal codice sorgente della pagina nel browser che `_framework/blazor.web.js` sia referenziato e che sia presente un elemento `blazor-server-component-state`.

**`Address already in use` / la porta è occupata.**

```powershell
Get-NetTCPConnection -LocalPort 5226 -State Listen | Select-Object OwningProcess
Stop-Process -Id <pid> -Force
```

**`FileNotFoundException` per `app.json` all'avvio.**
La configurazione viene letta da `AppContext.BaseDirectory\config`, cioè **accanto all'eseguibile compilato**, non dalla radice del repository. Il file `.csproj` copia lì `config/*.json` durante la build. Se l'output è stato spostato a mano, occorre copiare anche `config/`.

**`password authentication failed for user "postgres"`.**
L'app è ricaduta su `config/database.json`, che contiene `changeme` per progettazione. Verificare che `config/database.local.json` esista e sia JSON valido — una virgola finale invalida silenziosamente il file e l'override viene ignorato.

**`permission denied to create database`.**
Al ruolo configurato manca `CREATEDB`. Occorre concederglielo, oppure impostare `"AutoCreateDatabase": false` e provisionare con `Scripts/create_database.ps1`.

**Il cut-off si comporta in modo inatteso.**
Le regole usano l'**ora locale del server**, le colonne di audit usano UTC. Su questa macchina l'ora locale è CEST (UTC+2), quindi una riga scritta alle 09:00 locali viene memorizzata come `07:00Z`. È corretto, non è un bug.

**Il selettore della lingua compare due volte.**
Un bug già risolto, ma vale la pena riconoscerlo se dovesse tornare: `ConfigurationBinder` *aggiunge* elementi a una `ICollection<T>` predefinita già popolata invece di sostituirla, per cui `SupportedCultures` accumulava duplicati. Gestito con una de-duplicazione tramite `PostConfigure` in `Program.cs`.

**I test falliscono con un errore di connessione.**
PostgreSQL non è in esecuzione (`Get-Service postgresql-x64-16`) oppure `config/database.local.json` è errato. I test di concorrenza non sono progettati per essere saltati.

---

## 10. Lacune note al 13 agosto 2026

Sono reali, verificate e ancora aperte — non ipotesi.

> **Chiusi il 13 agosto 2026:** il mailer non collegato a PostgreSQL; il mailer che non leggeva gli override `*.local.json`; lo scheduler in-app mai registrato; i motivi dei giorni saltati che non usavano `ReasonCode`; la distinzione tra eliminazione e disattivazione dedotta tramite `IReportService`; e il segnaposto `UnitTest1.cs`. Vedere §6 e i riepiloghi degli agenti.

1. **Nessun dettaglio SMTP disponibile finora.** `config/email.json` ha `Mode: "PickupDirectory"` e una lista `Recipients` vuota, quindi un'esecuzione reale (non dry-run) riserva la giornata, costruisce il riepilogo a partire dai dati reali, poi si ferma con *"No recipients configured"* e registra `Failed`. È solo questione di configurazione — non serve alcuna modifica al codice una volta noti l'host e la lista dei destinatari.
2. **Due flussi dell'interfaccia non sono mai stati provati da una persona**, ma solo da build, test e un prerender lato server: l'elenco dei motivi di esclusione del *"Applica Menu N"* sull'intera settimana, e la conferma di eliminazione contro disattivazione nell'amministrazione. Entrambi sono stati riscritti il 13 agosto 2026 per usare i contratti propri del servizio. Sono coperti dal walkthrough **V4** ancora in sospeso — vedere lo script nel §6 del riepilogo dell'agente frontend.
3. **Ogni dipendente creato dal seeding ha attualmente esattamente una prenotazione**, quindi il ramo di *eliminazione* nell'amministrazione (dipendente senza storico) non è raggiungibile con i dati dimostrativi così come sono. Registrare un nuovo dipendente dalla pagina di prenotazione per ottenerne uno, poi provare a rimuoverlo.
4. **Il conto alla rovescia del cut-off non scorre in tempo reale** — viene calcolato al caricamento della pagina, quindi il tempo rimanente si aggiorna solo quando la pagina viene ricaricata.
5. **Il fallback di ricerca insensibile agli accenti** (per un server privo di `unaccent`) non ha alcun test automatico. Qui `unaccent` *è* installato, quindi il percorso esercitato è quello principale.
6. **Non è stato eseguito alcun controllo automatico di accessibilità o contrasto.** Il livello AA è stato tenuto presente in fase di progettazione — token, anelli di focus, ruoli ARIA, navigazione da tastiera — ma non è stato verificato da una macchina.
