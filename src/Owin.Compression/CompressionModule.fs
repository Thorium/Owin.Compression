namespace Owin

open System
open System.IO
open System.IO.Compression
open Owin
open Microsoft.Owin
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
    /// Default settings for compression.
    let DefaultCompressionSettings = {
        ServerPath = __SOURCE_DIRECTORY__;
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

    let internal compress (context:IOwinContext) (settings:CompressionSettings) (mode:ResponseMode) =
        let cancellationToken = context.Request.CallCancelled

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
                    return true, bytes
                | false -> 
                    let lastmodified = File.GetLastWriteTimeUtc(unpacked).ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", System.Globalization.CultureInfo.InvariantCulture)
                    context.Response.Headers.Add("Last-Modified", [|lastmodified|])
                    if String.IsNullOrEmpty context.Response.ETag then
                        context.Response.ETag <- (bytes.LongLength.ToString() + lastmodified + unpacked).GetHashCode().ToString()
                    return false, bytes
            }


        let encodings = context.Request.Headers.["Accept-Encoding"]
        let encodeOutput (enc:SupportedEncodings) = 

            match settings.CacheExpireTime with
            | Some d -> context.Response.Expires <- Nullable(d)
            | None -> ()

            match mode with
            | File ->
                async {
                    if cancellationToken.IsCancellationRequested then ()
                    use output = new MemoryStream()
                    let! awaited = getFile()
                    let shouldskip, bytes = awaited
                    if(shouldskip) then
                            return! context.Response.WriteAsync(bytes, cancellationToken) |> awaitTask
                    else
                    
                    if not(context.Response.Headers.ContainsKey "Vary") then
                        context.Response.Headers.Add("Vary", [|"Accept-Encoding"|])
                    use zipped = 
                        match enc with
                        | Deflate -> 
                            context.Response.Headers.Add("Content-Encoding", [|"deflate"|])
                            new DeflateStream(output, CompressionMode.Compress) :> Stream
                        | GZip -> 
                            context.Response.Headers.Add("Content-Encoding", [|"gzip"|])
                            new GZipStream(output, CompressionMode.Compress) :> Stream
                    let! t1 = zipped.WriteAsync(bytes, 0, bytes.Length, cancellationToken) |> awaitTask
                    t1 |> ignore
                    zipped.Close()
                    let op = output.ToArray()
                    return! context.Response.WriteAsync(op, cancellationToken) |> awaitTask
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
                                            |> Seq.map snd 
                                            |> Seq.contains(contentType) then 
                                        context.Response.Body <- buffer
                                        return true
                                    else
                                        return false
                        }
                    if usecompress then
                        if String.IsNullOrEmpty context.Response.ETag then
                            context.Response.ETag <- stream.GetHashCode().ToString()

                        match (not context.Response.Body.CanSeek) || (not context.Response.Body.CanRead) 
                              || context.Response.Body.Length < settings.MinimumSizeToCompress with
                        | true -> 
                            if context.Response.Body.CanSeek then
                                context.Response.Body.Seek(0L, SeekOrigin.Begin) |> ignore
                        
                            do! context.Response.Body.CopyToAsync(stream, defaultBufferSize, cancellationToken) |> awaitTask
                        | false -> 

                            let canStream = String.Equals(context.Request.Protocol, "HTTP/1.1", StringComparison.Ordinal)

                            if not(context.Response.Headers.ContainsKey "Vary") then
                                context.Response.Headers.Add("Vary", [|"Accept-Encoding"|])

                            use output = new MemoryStream()

                            use zipped = 
                                match enc with
                                | Deflate -> 
                                    context.Response.Headers.Add("Content-Encoding", [|"deflate"|])
                                    new DeflateStream(output, CompressionMode.Compress) :> Stream
                                | GZip -> 
                                    context.Response.Headers.Add("Content-Encoding", [|"gzip"|])
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
                                           || context.Response.Headers.["Transfer-Encoding"] <> "chunked" then
                                            context.Response.Headers.["Transfer-Encoding"] <- "chunked"
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
                        let! _, r = getFile()
                        return context.Response.WriteAsync(r, cancellationToken) |> Async.AwaitTask
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
    static member UseCompressionModule(app:IAppBuilder, settings:CompressionSettings) =
        app.Use(fun context next ->
            (compress context settings (ResponseMode.ContextResponseBody(next)) )()
        )

    [<Extension>]
    static member UseCompressionModule(app:IAppBuilder) =
        CompressionExtensions.UseCompressionModule(app, DefaultCompressionSettings)

    /// You can set a path that is url that will be captured.
    /// The subsequent url-path will be mapped to server path.
    [<Extension>]
    static member MapCompressionModule(app:IAppBuilder, path:string, settings:CompressionSettings) =
        app.Map(path, fun ap ->
         ap.Run(fun context ->
            (compress context settings ResponseMode.File)() 
            |> awaitTask |> Async.StartAsTask :> _
        ))

    /// You can set a path that is url that will be captured.
    /// The subsequent url-path will be mapped to server path.
    /// Uses OwinCompression.DefaultCompressionSettings
    [<Extension>]
    static member MapCompressionModule(app:IAppBuilder, path:string) =
        CompressionExtensions.MapCompressionModule(app, path, DefaultCompressionSettings)
