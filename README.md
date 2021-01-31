![Wiz](assets/wiz-banner.png)

<br /><br />

*The F# web framework designed for clarity and speed*

→ [https://wiz.run](https://wiz.run)

<br /><br />

```fsharp
open Wiz.Server
open Wiz.Route
open Wiz.Context

let handler ctx =
  ctx |> sendText "Hello, World"

genServer()
|> setRoutes [get "/" handler]
|> run
```

<br /><br />

## Install

Via the .Net CLI:

```bash
dotnet add package Wiz
```

## Example

Here is a fully functional server with 2 routes:

```fsharp
module Program
  open Wiz.Server
  open Wiz.Route
  open Wiz.Context

  ///////////////////////////
  // define route handlers
  ///////////////////////////
  let handleHome ctx =
    ctx
    |> sendText "Show me some magic!"

  let handleSpell ctx =
    ctx
    |> sendText "Abra Kadabra Kalamazoo!"

  ///////////////////////////
  // map routes to handlers
  ///////////////////////////
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

## About Wiz

Wiz is the result of attentively designing what an F# HTTP server API should look like, and then building that API.

Wiz's goal is to make it *SO* easy to interface with ASP.NET that its the most developer-friendly HTTP framework in all of .NET. To earn that title, Wiz needs to be well documented, intuitive, easily configurable, and fast as hell.


<br />

![Wiz Moon Logo](assets/wiz-icon.png)
