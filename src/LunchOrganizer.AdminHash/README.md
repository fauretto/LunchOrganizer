# LunchOrganizer.AdminHash

A small console tool for generating a `pbkdf2-sha256` password token to paste into
`config/admin-users.json`. It never reads or writes that file itself — you copy the
printed token in manually.

## Usage

Pass the password as an argument:

```
dotnet run --project src/LunchOrganizer.AdminHash -- "mypassword"
```

Or run with no argument for an interactive, masked prompt (each character you type
is echoed as `*`):

```
dotnet run --project src/LunchOrganizer.AdminHash
```

Either way, the tool prints the token on its own line, e.g.:

```
pbkdf2-sha256:210000:c2FsdGJhc2U2NA==:aGFzaGJhc2U2NA==
```

## Updating admin-users.json

Paste the printed token as the `Password` value for the corresponding user:

```json
{ "Username": "massimo", "DisplayName": "Massimo Fauro", "Password": "pbkdf2-sha256:210000:...:..." }
```
