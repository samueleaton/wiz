![Wiz](assets/wiz-banner.png)

<br /><br />

*The F# web framework designed for clarity and speed*

→ [https://wiz.run](https://wiz.run)

<br /><br />

## Install

Via the .Net CLI:

```bash
dotnet add package Wiz
```

## Example

Here is a simple server that has 2 routes:

```fsharp
module Program
  open Wiz.Server
  open Wiz.Route
  open Wiz.Context

  // define route handlers
  let handleHome ctx =
    ctx
    |> sendText "Show me some magic!"

  let handleSpell ctx =
    ctx
    |> sendText "Abra Kadabra Kalamazoo!"

  // map routes to handlers
  let routes = [
    get "/" handleHome
    get "/spell" handleSpell
  ]

  [<EntryPoint>]
  let main args =
    genServer()
    |> setRoutes routes
    |> run
```
