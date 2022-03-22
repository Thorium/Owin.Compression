#if INTERACTIVE
#r "../../packages/standard/Microsoft.AspNetCore.Http.Abstractions/lib/netstandard2.0/Microsoft.AspNetCore.Http.Abstractions.dll"
#r "../../packages/standard/Microsoft.AspNetCore.Http.Features/lib/netstandard2.0/Microsoft.AspNetCore.Http.Features.dll"
#r "../../packages/standard/Microsoft.Extensions.Primitives/lib/netstandard2.0/Microsoft.Extensions.Primitives.dll"
#r @"C:\Program Files\dotnet\sdk\2.0.0\Microsoft\Microsoft.NET.Build.Extensions\net461\lib\netstandard.dll"
#else
namespace Owin
#endif

open System
open System.IO
open System.IO.Compression
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.Abstractions
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Primitives
open System.Threading.Tasks
open System.Runtime.CompilerServices
open System.Collections.Generic

/// Supported compression methods
type SupportedEncodings =
| Deflate
| GZip

/// Do you fetch files or do you encode context.Response.Body?
type ResponseMode =
| File
| ContextResponseBody of Next: Func<Task>

/// Settings for compression.
type CompressionSettings = {
    ServerPath: string;
    AllowUnknonwnFiletypes: bool;
    AllowRootDirectories: bool;
    CacheExpireTime: DateTimeOffset option;
    AllowedExtensionAndMimeTypes: IEnumerable<string*string>;
    MinimumSizeToCompress: int64;
    DeflateDisabled: bool;
    }

module OwinCompression =
#if INTERACTIVE
    let basePath = __SOURCE_DIRECTORY__;
#else
    let basePath = System.Reflection.Assembly.GetExecutingAssembly().Location |> Path.GetDirectoryName
#endif
    /// Default settings for compression.
    let DefaultCompressionSettings = {
        ServerPath = basePath;
        AllowUnknonwnFiletypes = false;
        AllowRootDirectories = false;
        CacheExpireTime = None
        MinimumSizeToCompress = 1000L
        DeflateDisabled = false
        AllowedExtensionAndMimeTypes = 
        [|
            ".js"   , "application/javascript";
            ".css"  , "text/css";
            ".yml"  , "application/x-yaml";
            ".json" , "application/json";
            ".svg"  , "image/svg+xml";
            ".txt"  , "text/plain";
            ".html" , "application/json"; // we don't want to follow hyperlinks, so not "text/html"
            ".map"  , "application/octet-stream";
            ".ttf"  , "application/x-font-ttf";
            ".otf"  , "application/x-font-opentype";
        |]
    }

    /// Default settings with custom path. No cache time.
    let DefaultCompressionSettingsWithPath(path) = 
        {DefaultCompressionSettings with 
            ServerPath = path; CacheExpireTime = Some (DateTimeOffset.Now.AddDays 7.) }

    /// Default settings with custom path and cache-time. C#-helper method.
    let DefaultCompressionSettingsWithPathAndCache(path,cachetime) = 
        {DefaultCompressionSettings with ServerPath = path; CacheExpireTime = Some (cachetime) }

    let internal awaitTask = Async.AwaitIAsyncResult >> Async.Ignore
    let private defaultBufferSize = 81920

    let internal compress (context:HttpContext) (settings:CompressionSettings) (mode:ResponseMode) =
        let cancellationSrc = new System.Threading.CancellationTokenSource()
        let cancellationToken = cancellationSrc.Token

        let getMd5Hash (item:Stream) =
            let hasPos = 
                if item.CanSeek && item.Position > 0L then
                    let tmp = item.Position
                    item.Position <- 0L
                    Some tmp
                else None
            use md5 = System.Security.Cryptography.MD5.Create()
            let res = BitConverter.ToString(md5.ComputeHash(item)).Replace("-","")
            match hasPos with
            | Some x when item.CanSeek -> item.Position <- x
            | _ -> ()
            res

        let create304Response() =
            if cancellationSrc<>null then cancellationSrc.Cancel()
            context.Response.StatusCode <- 304
            context.Response.Body.Close()
            context.Response.Body <- Stream.Null
            context.Response.ContentLength <- Nullable()
            false

        let checkNoValidETag (itemToCheck:Stream) =
            if context.Request.Headers.ContainsKey("If-None-Match") && context.Request.Headers.["If-None-Match"] <> StringValues.Empty
               && (not(context.Request.Headers.ContainsKey("Pragma")) || context.Request.Headers.["Pragma"] <> StringValues("no-cache")) then
                if context.Request.Headers.["If-None-Match"] = context.Response.Headers.["ETag"] then
                    create304Response()
                else
                
                let etag = getMd5Hash(itemToCheck)
                if context.Request.Headers.["If-None-Match"] = StringValues(etag) then
                    create304Response()
                else
                    if context.Response.Headers.["ETag"] = StringValues.Empty then
                        context.Response.Headers.["ETag"] <- StringValues(etag)
                    true
            else
                if context.Response.Headers.["ETag"] = StringValues.Empty then
                    context.Response.Headers.["ETag"] <- StringValues(getMd5Hash(itemToCheck))
                true

        let getFile() =
            let unpacked :string = 
                    let p = context.Request.Path.ToString()
                    let p2 = match p.StartsWith("/") with true -> p.Substring(1) | false -> p
                    if not(settings.AllowRootDirectories) && p.Contains("..") then failwith "Invalid path"
                    if File.Exists p then failwith "Invalid resource"
                    Path.Combine ([| settings.ServerPath; p2|])

            let extension = unpacked.Substring(unpacked.LastIndexOf ".")
            let typemap = settings.AllowedExtensionAndMimeTypes |> Map.ofSeq

            match typemap.ContainsKey(extension) with
            | true -> context.Response.ContentType <- typemap.[extension]
            | false when settings.AllowUnknonwnFiletypes -> ()
            | _ -> failwith "Invalid resource type"

            async {
                use strm = File.OpenText unpacked
                let! txt = strm.ReadToEndAsync() |> Async.AwaitTask
                let bytes = txt |> System.Text.Encoding.UTF8.GetBytes
                match FileInfo(unpacked).Length < settings.MinimumSizeToCompress with
                | true -> 
                    return false, bytes
                | false -> 
                    let lastmodified = File.GetLastWriteTimeUtc(unpacked).ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", System.Globalization.CultureInfo.InvariantCulture)
                    context.Response.Headers.Add("Last-Modified", StringValues(lastmodified))
                    if checkNoValidETag(strm.BaseStream) then
                        return true, bytes
                    else
                        return false, null
            }

        let encodings = 
            if cancellationToken.IsCancellationRequested then "" 
            else context.Request.Headers.["Accept-Encoding"].ToString()
        let encodeOutput (enc:SupportedEncodings) = 

            match settings.CacheExpireTime with
            | Some d -> context.Response.Headers.["Expires"] <- StringValues(d.ToString())
            | None -> ()

            match mode with
            | File ->
                async {
                    if cancellationToken.IsCancellationRequested then ()
                    use output = new MemoryStream()
                    let! awaited = getFile()
                    let shouldskip, bytes = awaited
                    if(shouldskip) then
                            return! context.Response.WriteAsync(System.Text.Encoding.Default.GetString(bytes), cancellationToken) |> awaitTask
                    else
                    
                    if not(context.Response.Headers.ContainsKey "Vary") then
                        context.Response.Headers.Add("Vary", StringValues("Accept-Encoding"))
                    use zipped = 
                        match enc with
                        | Deflate -> 
                            context.Response.Headers.Add("Content-Encoding", StringValues("deflate"))
                            new DeflateStream(output, CompressionMode.Compress) :> Stream
                        | GZip -> 
                            context.Response.Headers.Add("Content-Encoding", StringValues("gzip"))
                            new GZipStream(output, CompressionMode.Compress) :> Stream
                    let! t1 = zipped.WriteAsync(bytes, 0, bytes.Length, cancellationToken) |> awaitTask
                    t1 |> ignore
                    zipped.Close()
                    let op = output.ToArray()
                    return! context.Response.WriteAsync(System.Text.Encoding.Default.GetString(op), cancellationToken) |> awaitTask
                } |> Async.StartAsTask :> Task

            | ContextResponseBody(next) ->
                async {
                    let compressableExtension() = 
                        match context.Request.Path.ToString() with
                        | null -> true
                        | x when x.Contains(".") -> 
                            let typemap = settings.AllowedExtensionAndMimeTypes |> Map.ofSeq
                            typemap.ContainsKey(x.Substring(x.LastIndexOf "."))
                        | _ -> false

                    if cancellationToken.IsCancellationRequested then 
                        do! next.Invoke() |> awaitTask
                        ()
                    else

                    use stream = context.Response.Body
                    use buffer = new MemoryStream()
                    
                    let! usecompress =
                        async {
                            if compressableExtension() || not(context.Request.Path.ToString().Contains("/signalr/")) then
                                context.Response.Body <- buffer // stream
                                do! next.Invoke() |> awaitTask
                                return true
                            else
                                do! next.Invoke() |> awaitTask
                                if compressableExtension() then // non-stream, but Invoke can change "/" -> "index.html"
                                    context.Response.Body <- buffer
                                    return true
                                elif String.IsNullOrEmpty context.Response.ContentType then 
                                    return false
                                else 
                                    let contentType = 
                                        // We are not interested of charset, etc:
                                        match context.Response.ContentType.Contains(";") with
                                        | false -> context.Response.ContentType.ToLower()
                                        | true -> context.Response.ContentType.Split(';').[0].ToLower()
                                    if settings.AllowedExtensionAndMimeTypes
                                            |> Seq.map snd |> Seq.append ["text/html"]
                                            |> Seq.contains(contentType) then 
                                        context.Response.Body <- buffer
                                        return true
                                    else
                                        return false
                        }

                    if usecompress && checkNoValidETag(context.Response.Body) then
                        let isAlreadyCompressed = not(String.IsNullOrWhiteSpace(context.Response.Headers.["Content-Encoding"]));
                        match (not context.Response.Body.CanSeek) || (not context.Response.Body.CanRead) 
                              || context.Response.Body.Length < settings.MinimumSizeToCompress
                              || isAlreadyCompressed with
                        | true -> 
                            if context.Response.Body.CanSeek then
                                context.Response.Body.Seek(0L, SeekOrigin.Begin) |> ignore
                        
                            do! context.Response.Body.CopyToAsync(stream, defaultBufferSize, cancellationToken) |> awaitTask
                        | false -> 

                            let canStream = String.Equals(context.Request.Protocol, "HTTP/1.1", StringComparison.Ordinal)

                            if not(context.Response.Headers.ContainsKey "Vary") then
                                context.Response.Headers.Add("Vary", StringValues("Accept-Encoding"))

                            use output = new MemoryStream()

                            use zipped = 
                                match enc with
                                | Deflate -> 
                                    context.Response.Headers.Add("Content-Encoding", StringValues("deflate"))
                                    new DeflateStream(output, CompressionMode.Compress) :> Stream
                                | GZip -> 
                                    context.Response.Headers.Add("Content-Encoding", StringValues("gzip"))
                                    new GZipStream(output, CompressionMode.Compress) :> Stream
                            //let! t1 = zipped.WriteAsync(bytes, 0, bytes.Length, cancellationToken) |> awaitTask
                            if context.Response.Body.CanSeek then
                                context.Response.Body.Seek(0L, SeekOrigin.Begin) |> ignore

                            do! context.Response.Body.CopyToAsync(zipped, defaultBufferSize, cancellationToken) |> awaitTask
                        
                            zipped.Close()
                            let op = output.ToArray()

                            if not(cancellationToken.IsCancellationRequested) then
                                try
                                    if canStream then
                                        if not(context.Response.Headers.ContainsKey("Transfer-Encoding")) 
                                           || context.Response.Headers.["Transfer-Encoding"] <> StringValues("chunked") then
                                            context.Response.Headers.["Transfer-Encoding"] <- StringValues("chunked")
                                    else
                                        context.Response.ContentLength <- Nullable(op.LongLength)
                                with | _ -> () // Content length info is not so important...

                            use tmpOutput = new MemoryStream(op)
                            if tmpOutput.CanSeek then
                                tmpOutput.Seek(0L, SeekOrigin.Begin) |> ignore
                        
                            do! tmpOutput.CopyToAsync(stream, defaultBufferSize, cancellationToken) |> awaitTask
                        return ()
                } |> Async.StartAsTask :> Task

        let encodeTask() =
            let WriteAsyncContext() =
                match mode with
                | File ->
                    async{
                        let! comp, r = getFile()
                        if comp then return context.Response.WriteAsync(System.Text.Encoding.Default.GetString(r), cancellationToken) |> Async.AwaitTask
                        else return Async.Sleep 50
                    } |> Async.StartAsTask :> Task
                | ContextResponseBody(next) ->
                    next.Invoke()
            if String.IsNullOrEmpty(encodings) then WriteAsyncContext()
            elif encodings.Contains "deflate" && not(settings.DeflateDisabled) then encodeOutput Deflate
            elif encodings.Contains "gzip" then encodeOutput GZip
            else WriteAsyncContext()

        encodeTask

open OwinCompression

[<Extension>]
type CompressionExtensions =

    [<Extension>]
    static member UseCompressionModule(app:IApplicationBuilder, settings:CompressionSettings) =
        app.Use(fun context next ->
            (compress context settings (ResponseMode.ContextResponseBody(next)) )()
        )

    [<Extension>]
    static member UseCompressionModule(app:IApplicationBuilder) =
        CompressionExtensions.UseCompressionModule(app, DefaultCompressionSettings)

    /// You can set a path that is url that will be captured.
    /// The subsequent url-path will be mapped to server path.
    [<Extension>]
    static member MapCompressionModule(app:IApplicationBuilder, path:PathString, settings:CompressionSettings) =
        app.Map(path, fun ap ->
         ap.Run(fun context ->
            (compress context settings ResponseMode.File)() 
            |> awaitTask |> Async.StartAsTask :> _
        ))

    /// You can set a path that is url that will be captured.
    /// The subsequent url-path will be mapped to server path.
    /// Uses OwinCompression.DefaultCompressionSettings
    [<Extension>]
    static member MapCompressionModule(app:IApplicationBuilder, path:PathString) =
        CompressionExtensions.MapCompressionModule(app, path, DefaultCompressionSettings)
