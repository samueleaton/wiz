namespace Wiz

open System
open System.IO
open System.Text
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Net.Http.Headers
open Microsoft.Extensions.Primitives
open System.Threading.Tasks
open Microsoft.AspNetCore.Server
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Options
open Microsoft.AspNetCore.HostFiltering
open System.Collections.Generic
open Microsoft.Extensions.Logging

type Method = GET | PUT | POST | DELETE | HEAD | INFO | OPTIONS

type StaticFileConfig = {
  ServeStaticFiles: bool;
  ServeIndexFiles: bool;
  StaticDirectoryRoot: string;
  StaticRequestPathPrefix: string
}

type HttpHandler = HttpContext -> Task
type Route = Method * string * HttpHandler
type DefaultStatusHandlers = Map<int, (HttpContext -> HttpContext)>

type ServerConfig = {
  AddServerHeader: bool;
  Host: string;
  Port: int;
  StaticFileConfig: StaticFileConfig;
  UseCompression: bool;
  Routes : Route list;
  DefaultStatusHandlers : DefaultStatusHandlers
}

type CookieOptions = {
  Domain : string;
  HttpOnly : bool;
  IsEssential : bool;
  MaxAge : TimeSpan;
  Path : string;
  SameSite : Microsoft.AspNetCore.Http.SameSiteMode;
  Secure : bool;
}

module Context =
  open Microsoft.AspNetCore.Http.Extensions

  let send (body: string) (ctx : HttpContext) =
    let bytes = (new System.Text.UTF8Encoding()).GetBytes body
    let byteLen = bytes.Length
    ctx.Response.ContentLength <- Nullable<int64>(byteLen |> int64)
    ctx.Response.Body.WriteAsync(bytes, 0, byteLen)

  let setStatusCode n (ctx : HttpContext) =
    ctx.Response.StatusCode <- n
    ctx

  let setContentType t (ctx : HttpContext) =
    ctx.Response.ContentType <- t
    ctx

  let getContentType (ctx : HttpContext) =
    match ctx.Request.ContentType with
    | null -> None
    | ct ->
      match ct.Trim() with
      | "" -> None
      | _ -> Some ct

  let getHeader (k : string) (ctx : HttpContext) =
    let header = ctx.Request.Headers.[k]
    match header.Count with
    | 0 -> None
    | _ ->
      let h = header.ToString()
      match h.Trim() with
      | "" -> None
      | _ -> Some h

  let getRemoteIpAddress (ctx : HttpContext) =
    ctx.Connection.RemoteIpAddress

  let setHeader (k : string) (v : string) (ctx : HttpContext) =
    ctx.Response.Headers.[k] <- StringValues(v)
    ctx

  let appendHeader (k : string) (v : string) (ctx : HttpContext) =
    ctx.Response.Headers.Append(k, StringValues(v))
    ctx

  let removeHeader (k : string) (ctx : HttpContext) =
    ctx.Response.Headers.Remove(k) |> ignore
    ctx

  let getHost (ctx : HttpContext) =
    ctx.Request.Host.Value

  let getPort (ctx : HttpContext) : Option<int> =
    let p = ctx.Request.Host.Port
    match p.HasValue with
    | false -> None
    | true -> Some p.Value

  let getHostName (ctx : HttpContext) =
    ctx.Request.Host.Host

  let getCookie (k : string) (ctx : HttpContext): string option =
    let cookie = ctx.Request.Cookies.[k]
    match cookie with
    | null -> None
    | c ->
      match c with
      | "" -> None
      | _ -> Some c

  let genCookieOptions ctx =
    // "max age" (relative time) is used in favor of "expires" (absolute time)
    {
      Domain = ctx |> getHostName;
      HttpOnly = false;
      IsEssential = false;
      MaxAge = new TimeSpan(30, 0, 0, 0); // 30 days
      Path = "/";
      SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Unspecified;
      Secure = false
    }
  let setCookieMaxAge age (options : CookieOptions) =
    {options with MaxAge = age}
  let setCookiePath path (options : CookieOptions) =
    {options with Path = path}
  let setCookieHttpOnly v (options : CookieOptions) =
    {options with HttpOnly = v}
  let setCookieSecure v (options : CookieOptions) =
    {options with Secure = v}

  let setCookie (k : string) (v : string) (options : CookieOptions) (ctx : HttpContext) =
    let setCookieOpts = new Microsoft.AspNetCore.Http.CookieOptions()

    setCookieOpts.Domain <- options.Domain
    setCookieOpts.HttpOnly <- options.HttpOnly
    setCookieOpts.IsEssential <- options.IsEssential
    setCookieOpts.MaxAge <- options.MaxAge
    setCookieOpts.Path <- options.Path
    setCookieOpts.SameSite <- options.SameSite
    setCookieOpts.Secure <- options.Secure

    ctx.Response.Cookies.Append(k, v, setCookieOpts)
    ctx

  let removeCookie (k : string) (ctx : HttpContext) =
    ctx.Response.Cookies.Delete(k)
    ctx

  let getMethod (ctx : HttpContext) : string =
    ctx.Request.Method

  let getScheme (ctx : HttpContext) : string =
    ctx.Request.Scheme

  let getPath (ctx : HttpContext) : string =
    let p = ctx.Request.Path
    match p.HasValue with
    | false -> "/"
    | true ->
      match p.Value with
      | "" -> "/"
      | path -> path

  let getQueryString (ctx : HttpContext) : string =
    let qs = ctx.Request.QueryString
    match qs.HasValue with
    | false -> ""
    | true -> qs.Value

  let hasQueryParam k (ctx : HttpContext) =
    ctx.Request.Query.ContainsKey(k)

  let getUrl ctx : string =
    getPath ctx + getQueryString ctx

  let getEncodedUrl (ctx : HttpContext) : string =
    ctx |> getUrl |> System.Uri.EscapeDataString

  let getOrigin ctx =
    getScheme ctx + "://" + getHost ctx

  let getHref ctx =
    getOrigin ctx + getUrl ctx

  let getQueryParam k (ctx : HttpContext) =
    let v = ctx.Request.Query.[k]
    match v.Count = 0 || StringValues.IsNullOrEmpty(v) with
    | true -> None
    | _ ->
      match v.ToString() with
      | "" -> None
      | value -> Some value

  let getQueryParams k (ctx : HttpContext) =
    let v = ctx.Request.Query.[k]
    match v.Count = 0 || StringValues.IsNullOrEmpty(v) with
    | true -> None
    | _ ->
      match v.ToString() with
      | "" -> None
      | _ -> Some v

  let getRouteParam k (ctx : HttpContext) =
    let v = ctx.Request.RouteValues.[k]
    match v with
    | null -> None
    | obj ->
      match obj.ToString() with
      | "" -> None
      | str -> Some str

  let getBody (ctx : HttpContext) =
    (new System.IO.StreamReader(ctx.Request.Body)).ReadToEndAsync()
    |> Async.AwaitTask
    |> Async.RunSynchronously

  let flush (ctx : HttpContext) =
    ctx.Response.CompleteAsync()

  let sendText body (ctx : HttpContext) =
    ctx
    |> setContentType "text/plain; charset=UTF-8"
    |> send body

  let sendHtml body (ctx : HttpContext) =
    ctx
    |> setContentType "text/html; charset=UTF-8"
    |> send body

  let sendJson body (ctx : HttpContext) =
    ctx
    |> setContentType "application/json; charset=UTF-8"
    |> send body

  let redirect path ctx =
    ctx
    |> setStatusCode 302
    |> setHeader "Location" path

module Internal =
  let defaultHandler ctx =
    let iconSvg = """<svg width="100%" height="100%" viewBox="0 0 174 96" version="1.1" xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" xml:space="preserve" xmlns:serif="http://www.serif.com/" style="fill-rule:evenodd;clip-rule:evenodd;stroke-linejoin:round;stroke-miterlimit:2;"><path d="M58.748,93.822C57.798,94.488 57.085,94.82 56.609,94.82C55.563,94.82 54.85,94.393 54.47,93.537C51.618,87.833 49.241,80.132 47.34,70.435C47.245,70.055 47.102,69.865 46.912,69.865L46.057,70.435C43.49,74.999 40.519,79.229 37.144,83.127C33.769,87.025 31.036,89.782 28.944,91.398L25.807,93.822C24.856,94.678 24.096,95.106 23.525,95.106C22.48,95.106 21.767,94.63 21.386,93.68C19.295,89.687 16.871,81.939 14.114,70.435L12.83,64.589C11.024,56.223 9.194,49.52 7.34,44.482C5.486,39.443 4.084,36.496 3.133,35.64L1.707,34.357C0.852,34.072 0.424,33.596 0.424,32.931C0.424,31.6 2.397,30.174 6.342,28.653C10.287,27.132 13.353,26.371 15.54,26.371C17.726,26.371 19.176,27.251 19.889,29.009C20.602,30.768 21.719,35.688 23.24,43.769C27.233,65.064 30.133,75.712 31.939,75.712C32.509,75.712 33.151,75.213 33.864,74.214C34.577,73.216 35.433,72.004 36.431,70.578C37.429,69.152 38.784,66.68 40.495,63.163C42.206,59.645 43.062,56.555 43.062,53.893C43.062,53.418 42.729,51.683 42.064,48.688C41.398,45.694 40.424,42.794 39.14,39.99C37.857,37.185 36.835,35.593 36.074,35.213C34.363,34.262 33.508,33.454 33.508,32.788C33.508,31.647 35.457,30.293 39.354,28.724C43.252,27.155 46.77,26.371 49.907,26.371C51.333,26.371 52.331,27.227 52.902,28.938C54.328,33.406 56.039,41.44 58.035,53.038C60.602,67.869 62.979,75.284 65.165,75.284C65.926,75.284 67.067,74.214 68.588,72.075C70.109,69.936 71.606,66.823 73.08,62.735C74.553,58.647 75.29,55.153 75.29,52.254C75.29,49.354 75.171,47.12 74.934,45.551C74.696,43.983 73.864,42.153 72.438,40.061C71.012,37.97 69.396,36.757 67.59,36.425C65.783,36.092 64.88,35.498 64.88,34.642C64.88,33.311 66.853,31.6 70.798,29.508C74.744,27.417 78.808,26.371 82.991,26.371C86.984,26.371 88.98,30.364 88.98,38.35C88.98,48.332 86.033,58.599 80.139,69.152C74.244,79.705 67.114,87.928 58.748,93.822Z" style="fill:#ec8667;fill-rule:nonzero;"/><path d="M108.089,14.678L104.666,14.535C102.099,14.535 100.436,15.153 99.675,16.389C99.485,16.674 99.247,16.817 98.962,16.817C97.726,16.817 97.108,14.702 97.108,10.471C97.108,6.24 97.75,3.555 99.034,2.414C100.317,1.273 102.551,0.703 105.736,0.703C108.921,0.703 111.083,1.249 112.224,2.343C113.365,3.436 113.935,5.266 113.935,7.833C113.935,10.4 113.508,12.182 112.652,13.18C111.796,14.179 110.275,14.678 108.089,14.678ZM96.681,86.692L98.249,53.466C98.249,45.575 97.346,40.204 95.54,37.352C94.969,36.401 94.304,35.688 93.543,35.213C92.973,35.022 92.688,34.642 92.688,34.072C92.688,32.36 95.112,30.697 99.96,29.081C104.809,27.464 108.35,26.656 110.584,26.656C112.818,26.656 113.983,27.227 114.078,28.368C114.078,30.269 113.817,35.664 113.294,44.553C112.771,53.442 112.509,62.782 112.509,72.574C112.509,82.366 113.08,88.451 114.221,90.828C114.506,91.303 114.648,91.778 114.648,92.254C114.648,93.489 112.842,94.107 109.23,94.107C105.617,94.107 101.909,93.157 98.107,91.255C97.156,90.78 96.681,89.259 96.681,86.692Z" style="fill:#ec8667;fill-rule:nonzero;"/><path d="M155.148,28.225L170.691,27.797C171.927,27.797 172.545,28.32 172.545,29.366C172.545,31.267 169.55,36.876 163.561,46.193C157.572,55.51 152.367,63.638 147.946,70.578C143.525,77.518 141.315,81.701 141.315,83.127C141.315,84.078 142.646,84.553 145.308,84.553C147.97,84.553 150.299,84.149 152.296,83.341C154.292,82.533 155.766,81.463 156.716,80.132C158.332,77.851 159.14,75.759 159.14,73.858C159.14,71.956 158.95,70.103 158.57,68.296C158.57,67.156 159.616,66.49 161.707,66.3C166.651,66.3 169.931,67.678 171.547,70.435C172.783,72.622 173.401,74.856 173.401,77.138C173.401,82.271 171.309,86.122 167.126,88.688C162.943,91.255 158,92.539 152.296,92.539L136.039,92.111L121.636,92.396C119.925,92.396 119.069,91.802 119.069,90.614C119.069,89.425 120.614,86.312 123.704,81.273C126.793,76.235 129.907,71.481 133.044,67.013L137.75,60.311C145.736,47.762 149.99,40.988 150.513,39.99C151.036,38.991 151.297,38.112 151.297,37.352C151.297,36.021 149.919,35.355 147.162,35.355C144.405,35.355 141.909,35.735 139.675,36.496C137.441,37.257 135.611,38.183 134.185,39.277C132.759,40.37 131.523,41.487 130.477,42.628C128.576,44.719 127.625,46.05 127.625,46.621C127.34,47.571 126.817,48.047 126.057,48.047L125.914,48.047C124.583,48.047 123.918,47.239 123.918,45.623C123.918,41.915 124.773,37.542 126.484,32.503L127.483,29.936C127.863,28.605 128.861,27.94 130.477,27.94L155.148,28.225Z" style="fill:#ec8667;fill-rule:nonzero;"/></svg>"""
    let css = """
      <style>
        html, body {
          margin:0;padding:0;font-family:'pt mono', monospace;
          background-color:#FBFAEF;height:100%;overflow:hidden;
        }
        div.content {
          height:100%;margin:0;padding:10px;display:flex;align-items:center;
          justify-content:center;font-weight:500;box-sizing:border-box;box-shadow:inset 0 0 0 5px #ec8667;
        }
        div.content > span {
          display:flex; align-items:flex-end;
        }
        span.text {
          display:inline-block;margin-left:15px;font-size:15px;color:rgb(60,60,60);
        }
        svg {height:22px;width:auto;}
      </style>
    """
    let head = $"""
      <head>
        <title>Wiz</title>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        {css}
      </head>
    """
    let body = $"""
      <body>
        <div class="content">
          <span>{iconSvg}<span class="text">is running.</span></span>
        </div>
      </body>
    """
    let welcomeHtml = $"<!DOCTYPE html><html>{head}{body}</html>"
    ctx |> Context.sendHtml welcomeHtml

  let configureKestrel appConfig (serverOptions : Kestrel.Core.KestrelServerOptions) =
    serverOptions.AddServerHeader <- false
    serverOptions.Listen(
      System.Net.IPAddress.Parse(appConfig.Host),
      appConfig.Port
    )

  let wrapDelegate f = new RequestDelegate(f)

  let methodToString method =
    match method with
    | Method.GET -> "GET"
    | Method.PUT -> "PUT"
    | Method.POST -> "POST"
    | Method.DELETE -> "DELETE"
    | Method.HEAD -> "HEAD"
    | Method.INFO -> "INFO"
    | Method.OPTIONS -> "OPTIONS"

  // configure kestrel and add routes
  let configureWebHost appConf (webBuilder : Microsoft.AspNetCore.Hosting.IWebHostBuilder) =

    // let respondNotFound (ctx : HttpContext) =
    //   ctx.Response.StatusCode <- 404
    //   ctx.Response.ContentType <- "text/plain"
    //   ctx.Response.WriteAsync("Not Found!!!") |> ignore
    //   ctx

    // let defaultHandlerIfNone (statusCode : int) = Option.defaultValue (fun (ctx : HttpContext) ->
    //   ctx.Response.ContentType <- "text/plain"
    //   ctx.Response.WriteAsync(string statusCode) |> ignore
    //   ctx
    // )

    // let defaultStatusHandler = Func<RequestDelegate, RequestDelegate> (fun (next : RequestDelegate) ->
    //   RequestDelegate (fun (ctx : HttpContext) ->
    //     printfn "before..."
    //     next.Invoke(ctx).ContinueWith(fun t ->
    //       let acceptsHeader = ctx.Request.Headers.["Accept"]
    //       printfn $"Accepts: {acceptsHeader}"
    //       printfn $"Status: {t.Status}"
    //       printfn $"HasStarted: {ctx.Response.HasStarted}"
    //       printfn $"StatusCode: {ctx.Response.StatusCode}"
    //       printfn $"ContentType: {ctx.Response.ContentType}"
    //       printfn "after"
    //       let statusCode = ctx.Response.StatusCode
    //       if not ctx.Response.HasStarted && not (ctx.Response.StatusCode = 200) then
    //         let handler = appConf.DefaultStatusHandlers |> Map.tryFind statusCode |> defaultHandlerIfNone statusCode
    //         handler ctx |> ignore
    //         ()
    //       else
    //         ()
    //       // if t.Status = TaskStatus.Faulted then
    //       //     // crashed while running on thread pool
    //       //     exHandler ctx t.Exception
    //     )
    //     // with ex ->
    //     //     // crashed during invocation, before going on thread pool
    //     //     exHandler ctx ex
    //     //     Task.FromResult(false) :> Task
    //   )
    // )

    let sfc = appConf.StaticFileConfig

    webBuilder.ConfigureKestrel(configureKestrel appConf).Configure(fun appBuilder ->
      if appConf.UseCompression = true then
        appBuilder.UseResponseCompression() |> ignore

      if sfc.ServeStaticFiles = true then
        if sfc.ServeIndexFiles = true then
          let idxOpts = new DefaultFilesOptions()
          idxOpts.DefaultFileNames.Clear()
          idxOpts.DefaultFileNames.Add("index.html")
          idxOpts.RedirectToAppendTrailingSlash <- false
          appBuilder.UseDefaultFiles(idxOpts) |> ignore
        let staticOpts = new StaticFileOptions()
        staticOpts.RedirectToAppendTrailingSlash <- false
        staticOpts.RequestPath <- new PathString(sfc.StaticRequestPathPrefix)
        appBuilder.UseStaticFiles(staticOpts) |> ignore

      // appBuilder.Use(defaultStatusHandler) |> ignore
      appBuilder.UseRouting().UseEndpoints(fun endpoints ->
        if List.isEmpty appConf.Routes then
          endpoints.MapGet(
            "/", wrapDelegate(fun (ctx: HttpContext) -> defaultHandler ctx)
          ) |> ignore
        else
          for (httpMethod, path, handler) in appConf.Routes do
            endpoints.MapMethods(
              path,
              seq { methodToString httpMethod },
              wrapDelegate(fun (ctx: HttpContext) -> handler ctx)
            ) |> ignore
      ) |> ignore
    ).SuppressStatusMessages(true) |> ignore

    webBuilder.UseWebRoot(sfc.StaticDirectoryRoot) |> ignore


  let configureLogging appConf (builder : IHostBuilder) =
    builder.ConfigureLogging(fun (ctx: HostBuilderContext) (loggingBuilder: ILoggingBuilder) ->
      loggingBuilder.ClearProviders() |> ignore
      // loggingBuilder.Configure(fun options ->
      //   options.ActivityTrackingOptions = ActivityTrackingOptions.SpanId
      //                                       | ActivityTrackingOptions.TraceId
      //                                       | ActivityTrackingOptions.ParentId;
      // )
      loggingBuilder.AddConfiguration(ctx.Configuration.GetSection("Logging")) |> ignore
      // loggingBuilder.AddConsole() |> ignore
      // We don't like this
      loggingBuilder.AddDebug() |> ignore
      // loggingBuilder.AddEventSourceLogger();
    ) |> ignore

  let configureServices appConf (builder : IHostBuilder) =
    builder.ConfigureServices(fun (ctx: HostBuilderContext) (services: IServiceCollection) ->
      if appConf.UseCompression = true then
        services.AddResponseCompression() |> ignore
    ) |> ignore

  let createHostBuilder appConf =
    let builder = Host.CreateDefaultBuilder()
    builder |> configureLogging appConf |> ignore
    builder |> configureServices appConf |> ignore
    builder.ConfigureWebHostDefaults(Action<IWebHostBuilder>(configureWebHost appConf))

module Route =
  open Microsoft.AspNetCore.Http.Extensions

  let route meth path handler =
    Route (meth, path, handler)

  let get path handler =
    Route (GET, path, handler)

  let put path handler =
    Route (PUT, path, handler)

  let post path handler =
    Route (POST, path, handler)

  let head path handler =
    Route (HEAD, path, handler)

  let options path handler =
    Route (OPTIONS, path, handler)

  let delete path handler =
    Route (DELETE, path, handler)

module Server =
  let genEmptyDefaultStatusHandlers () =
    Map<int, (HttpContext -> HttpContext)> []

  let genServer () : ServerConfig =
    let staticFileConfig = {
      ServeStaticFiles = false;
      ServeIndexFiles = true;
      StaticDirectoryRoot = "wwwroot";
      StaticRequestPathPrefix = ""
    }
    { AddServerHeader = false;
      Host = "127.0.0.1";
      Port = 9999;
      StaticFileConfig = staticFileConfig;
      UseCompression = true;
      Routes = [Route.get "/" Internal.defaultHandler];
      DefaultStatusHandlers = genEmptyDefaultStatusHandlers() }

  let setPort port (app : ServerConfig) =
    { app with Port = port }

  let setHost host (app : ServerConfig) =
    { app with Host = host }

  let serveStaticFiles value (app : ServerConfig) =
    let staticConf = { app.StaticFileConfig with ServeStaticFiles = value }
    { app with StaticFileConfig = staticConf }

  let serveIndexFiles value (app : ServerConfig) =
    let staticConf = { app.StaticFileConfig with ServeIndexFiles = value }
    { app with StaticFileConfig = staticConf }

  let useCompression value (app : ServerConfig) =
    { app with UseCompression = value }

  let setStaticDirectory root (app : ServerConfig) =
    let staticConf = { app.StaticFileConfig with StaticDirectoryRoot = root }
    { app with StaticFileConfig = staticConf }

  let setStaticRequestPath str (app : ServerConfig) =
    let staticConf = { app.StaticFileConfig with StaticRequestPathPrefix = str }
    { app with StaticFileConfig = staticConf }

  let setRoutes routes app =
    {app with Routes = routes}

  let setDefaultStatusHandlers (handlers : (int * (HttpContext -> HttpContext)) list) app =
    let reducer (map : Map<int, (HttpContext -> HttpContext)>) (status : int, handler : (HttpContext -> HttpContext)) =
      Map.add status handler map
    let statusHandlers =  List.fold reducer app.DefaultStatusHandlers handlers
    {app with DefaultStatusHandlers = statusHandlers}

  let printConfig (appConf : ServerConfig) =
    printfn "%O" appConf
    appConf

  let run (appConf : ServerConfig) =
    let server = Internal.createHostBuilder(appConf).Build()

    let { Port = port; Host = host } = appConf
    let hst = if host = "localhost" || host = "127.0.0.1" then
                "localhost"
              else
                host
    printfn "🔮 Wiz listening on http://%s:%i" hst port

    server.Run()
    0
