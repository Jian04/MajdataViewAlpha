# Majdata Pet Launcher

`MajdataLauncher` is an independent WPF process. It starts View first, waits for
the local `8013` endpoint, then starts Edit. The pet remains alive when either
application reloads and can optionally follow the Edit window.

## Run

```powershell
dotnet run --project MajdataLauncher\MajdataLauncher.csproj
```

On first run, `launcher.json` is created beside the executable. Empty paths use
automatic discovery; release packages can set paths relative to the launcher.

## Pet Control

The launcher listens on `127.0.0.1:8015` by default. MajView, MajEdit, or an
agent can trigger expressions with plain HTTP:

```text
GET http://127.0.0.1:8015/pet?action=running&message=Writing%20chart%20ideas
GET http://127.0.0.1:8015/pet?action=star-combo&message=Checking%20star%20routes
GET http://127.0.0.1:8015/pet?action=look&angle=90
```

Supported actions: `idle`, `running`, `working`, `chart-agent`, `review`,
`organize`, `waiting`, `ask`, `failed`, `error`, `wave`, `success`, `jump`,
`launch`, `left`, `right`, `look`, and `star-combo`.

## Pet Packages

Pet packages live under `Pets/<id>/`. A v2 pet package uses:

```json
{
  "id": "dilaxiong",
  "displayName": "Dilaxiong",
  "description": "One short sentence.",
  "spriteVersionNumber": 2,
  "spritesheetPath": "spritesheet.png"
}
```

The atlas must be `1536x2288`, with `192x208` cells in an 8x11 grid. Rows 0-8
follow the Hatch Pet state order. Rows 9-10 provide the 16 look directions.
