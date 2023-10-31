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
[<Struct>]
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
    CacheExpireTime: DateTimeOffset voption;
    AllowedExtensionAndMimeTypes: IEnumerable<string*string>;
    MinimumSizeToCompress: int64;
    DeflateDisabled: bool;
    StreamingDisabled: bool;
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
        CacheExpireTime = ValueNone
        MinimumSizeToCompress = 1000L
        DeflateDisabled = false
        StreamingDisabled = false
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
            ".ico"  , "image/x-icon";
            ".map"  , "application/json";
            ".xml"  , "application/xml";
            ".xsl"  , "text/xml";
            ".xhtml", "application/xhtml+xml";
            ".rss"  , "application/rss+xml";
            ".eot"  , "font/eot";
            ".aspx" , "text/html";
        |]
    }

    /// Default settings with custom path. No cache time.
    let DefaultCompressionSettingsWithPath path = 
        {DefaultCompressionSettings with 
            ServerPath = path; CacheExpireTime = ValueSome (DateTimeOffset.Now.AddDays 7.) }

    /// Default settings with custom path and cache-time. C#-helper method.
    let DefaultCompressionSettingsWithPathAndCache(path,cachetime) = 
        {DefaultCompressionSettings with ServerPath = path; CacheExpireTime = ValueSome (cachetime) }

    let private defaultBufferSize = 81920

    module Internals =

        let getHash (item:Stream) =
            if item.CanRead then
                let hasPos = 
                    if item.CanSeek && item.Position > 0L then
                        let tmp = item.Position
                        item.Position <- 0L
                        ValueSome tmp
                    else ValueNone
                use md5 = System.Security.Cryptography.MD5.Create()
                let res = BitConverter.ToString(md5.ComputeHash item).Replace("-","")
                match hasPos with
                | ValueSome x when item.CanSeek -> item.Position <- x
                | _ -> ()
                ValueSome res
            else ValueNone

        let create304Response (contextResponse:HttpResponse) =
            if not contextResponse.HasStarted then
                contextResponse.StatusCode <- 304
                contextResponse.Body.Close()
                contextResponse.Body <- Stream.Null
                contextResponse.ContentLength <- Nullable()
            false

        let checkNoValidETag (contextRequest:HttpRequest) (contextResponse:HttpResponse) (cancellationSrc:Threading.CancellationTokenSource) (itemToCheck:Stream) =
            let ifNoneMatch, noneMatchValue =
                match contextRequest.Headers.TryGetValue "If-None-Match" with
                | true, nonem when nonem <> StringValues.Empty -> true, nonem
                | _ -> false, StringValues.Empty

            let hasNoPragma =
                match contextRequest.Headers.TryGetValue "Pragma" with
                | false, _ -> true
                | true, x when x <> StringValues("no-cache") -> true
                | true, _ -> false

            let etagVal =
                match contextResponse.Headers.TryGetValue "ETag" with
                | true, etag -> etag
                | false, _ -> StringValues.Empty
            if ifNoneMatch && hasNoPragma then
                if noneMatchValue = etagVal then
                    if not (isNull cancellationSrc) then cancellationSrc.Cancel()
                    create304Response contextResponse
                else
                
                match getHash itemToCheck with
                | ValueSome etag ->
                    if noneMatchValue = StringValues(etag) then
                        if not (isNull cancellationSrc) then cancellationSrc.Cancel()
                        create304Response contextResponse
                    else
                        if etagVal = StringValues.Empty &&
                                not contextResponse.Headers.IsReadOnly then
                            contextResponse.Headers.["ETag"] <- StringValues(etag)
                        true
                | ValueNone -> true
            else
                if etagVal = StringValues.Empty then
                    match getHash(itemToCheck) with
                    | ValueSome etag when not contextResponse.Headers.IsReadOnly ->
                        contextResponse.Headers.["ETag"] <- StringValues etag
                    | _ -> ()
                true

        let getFile (settings:CompressionSettings) (contextRequest:HttpRequest) (contextResponse:HttpResponse) (cancellationSrc:Threading.CancellationTokenSource) =
            let unpacked :string = 
                    let p = contextRequest.Path.ToString()
                    let p2 = match p.StartsWith("/") with true -> p.Substring(1) | false -> p
                    if not(settings.AllowRootDirectories) && p.Contains ".." then failwith "Invalid path"
                    if File.Exists p then failwith "Invalid resource"
                    Path.Combine ([| settings.ServerPath; p2|])

            let extension = if not (unpacked.Contains ".") then "" else unpacked.Substring(unpacked.LastIndexOf ".")
            let typemap = settings.AllowedExtensionAndMimeTypes |> Map.ofSeq

            match typemap.TryGetValue extension with
            | true, extval -> contextResponse.ContentType <- extval
            | false, _ when settings.AllowUnknonwnFiletypes -> ()
            | _ ->
                if not contextResponse.HasStarted then
                    contextResponse.StatusCode <- 415
                raise (ArgumentException("Invalid resource type", contextRequest.Path.ToString()))

            task {
                try
                    use strm = File.OpenText unpacked
                    let! txt = strm.ReadToEndAsync()
                    let bytes = txt |> System.Text.Encoding.UTF8.GetBytes
                    match FileInfo(unpacked).Length < settings.MinimumSizeToCompress with
                    | true -> 
                        return false, bytes
                    | false -> 
                        let lastmodified = File.GetLastWriteTimeUtc(unpacked).ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", System.Globalization.CultureInfo.InvariantCulture)
                        contextResponse.Headers.Add("Last-Modified", StringValues(lastmodified))
                        let etagCheck = checkNoValidETag contextRequest contextResponse cancellationSrc strm.BaseStream
                        if etagCheck then
                            return true, bytes
                        else
                            return false, null
                with
                | :? FileNotFoundException ->
                    if not contextResponse.HasStarted then
                        contextResponse.StatusCode <- 404
                    return false, null
            }

        let encodeFile (enc:SupportedEncodings) (settings:CompressionSettings) (contextRequest:HttpRequest) (contextResponse:HttpResponse) (cancellationSrc:Threading.CancellationTokenSource) =
            task {
                let cancellationToken = cancellationSrc.Token
                if cancellationToken.IsCancellationRequested then ()
                let! awaited = getFile settings contextRequest contextResponse cancellationSrc
                let shouldProcess, bytes = awaited
                if not shouldProcess then
                    if isNull bytes then
                        return ()
                    else
                        return! contextResponse.Body.WriteAsync(bytes, 0, bytes.Length, cancellationToken)
                else

                use output = new MemoryStream()
                if not(contextResponse.Headers.ContainsKey "Vary") then
                    contextResponse.Headers.Add("Vary", StringValues("Accept-Encoding"))
                use zipped = 
                    match enc with
                    | Deflate -> 
                        contextResponse.Headers.Add("Content-Encoding", StringValues("deflate"))
                        new DeflateStream(output, CompressionMode.Compress) :> Stream
                    | GZip -> 
                        contextResponse.Headers.Add("Content-Encoding", StringValues("gzip"))
                        new GZipStream(output, CompressionMode.Compress) :> Stream
                let! t1 = zipped.WriteAsync(bytes, 0, bytes.Length, cancellationToken)
                t1 |> ignore
                zipped.Close()
                let op = output.ToArray()

                let canStream = String.Equals(contextRequest.Protocol, "HTTP/1.1", StringComparison.Ordinal)

                let doStream = 
                    if cancellationToken.IsCancellationRequested then
                        false
                    else
                        try
                            if canStream && (int64 defaultBufferSize) < op.LongLength then
                                if not(contextResponse.Headers.ContainsKey("Transfer-Encoding")) 
                                    || contextResponse.Headers.["Transfer-Encoding"] <> StringValues("chunked") then
                                    contextResponse.Headers.["Transfer-Encoding"] <- "chunked"
                                true
                            else
                                contextResponse.ContentLength <- Nullable op.LongLength
                                false

                        with | _ -> 
                            false // Content length info is not so important...

                if doStream then
                    return! zipped.CopyToAsync(contextResponse.Body, defaultBufferSize, cancellationToken)
                else
                    return! contextResponse.Body.WriteAsync(op, 0, op.Length, cancellationToken)
            } :> Task


        let compressableExtension (settings:CompressionSettings) (path:string) =
            match path with
            | null -> true
            | x when x.Contains(".") -> 
                let typemap = settings.AllowedExtensionAndMimeTypes |> Map.ofSeq
                typemap.ContainsKey(x.Substring(x.LastIndexOf "."))
            | _ -> false

        let encodeStream (enc:SupportedEncodings) (settings:CompressionSettings) (contextRequest:HttpRequest) (contextResponse:HttpResponse) (cancellationSrc:Threading.CancellationTokenSource) (next:Func<Task>) =
            let cancellationToken = cancellationSrc.Token
            let originalLengthNotEnough = contextResponse.Body.CanRead && contextResponse.Body.Length < settings.MinimumSizeToCompress
            let checkCompressability buffer =
                let captureResponse() =
                    match buffer with
                    | Some bufferStream ->
                        contextResponse.Body <- bufferStream
                    | None -> ()
                let compressableExtension = compressableExtension settings (contextRequest.Path.ToString())
                if compressableExtension then // non-stream, but Invoke can change "/" -> "index.html"
                    captureResponse()
                    true
                elif String.IsNullOrEmpty contextResponse.ContentType then
                    if settings.AllowUnknonwnFiletypes then
                        captureResponse()
                        true
                    else false
                else 
                    let contentType = 
                        // We are not interested of charset, etc:
                        match contextResponse.ContentType.Contains(";") with
                        | false -> contextResponse.ContentType.ToLower()
                        | true -> contextResponse.ContentType.Split(';').[0].ToLower()
                    if settings.AllowedExtensionAndMimeTypes
                            |> Seq.map snd |> Seq.append ["text/html"]
                            |> Seq.contains(contentType) then 
                        captureResponse()
                        true
                    else
                        false

            let isCompressable =
                (checkCompressability None) && not(contextRequest.Path.ToString().Contains("/signalr/"))
                && contextResponse.Body.CanWrite

            let continuation2 (copy1:unit->Task) (copy2:Stream->Task) (copy3:MemoryStream->Task) = 
                task {

                    let noCompression =
                        (not contextResponse.Body.CanSeek) || (not contextResponse.Body.CanRead) 
                            || originalLengthNotEnough
                            || (contextResponse.Headers.ContainsKey("Content-Encoding") &&
                                not(String.IsNullOrWhiteSpace(contextResponse.Headers.["Content-Encoding"])))

                    match noCompression with
                    | true -> 
                        if contextResponse.Body.CanSeek then
                            contextResponse.Body.Seek(0L, SeekOrigin.Begin) |> ignore
                        do! copy1()
                        return ()
                    | false -> 

                        let canStream = String.Equals(contextRequest.Protocol, "HTTP/1.1", StringComparison.Ordinal) && not settings.StreamingDisabled

                        if not(contextResponse.Headers.ContainsKey "Vary") then
                            contextResponse.Headers.Add("Vary", StringValues("Accept-Encoding"))

                        use output = new MemoryStream()

                        use zipped = 
                            match enc with
                            | Deflate -> 
                                contextResponse.Headers.Add("Content-Encoding", StringValues("deflate"))
                                new DeflateStream(output, CompressionMode.Compress) :> Stream
                            | GZip -> 
                                contextResponse.Headers.Add("Content-Encoding", StringValues("gzip"))
                                new GZipStream(output, CompressionMode.Compress) :> Stream
                        //let! t1 = zipped.WriteAsync(bytes, 0, bytes.Length, cancellationToken)
                        if contextResponse.Body.CanSeek then
                            contextResponse.Body.Seek(0L, SeekOrigin.Begin) |> ignore

                        do! copy2(zipped)

                        zipped.Close()
                        let op = output.ToArray()

                        if not(cancellationToken.IsCancellationRequested) then
                            try
                                if canStream && (int64 defaultBufferSize) < op.LongLength then
                                    if not(contextResponse.Headers.ContainsKey("Transfer-Encoding")) 
                                        || contextResponse.Headers.["Transfer-Encoding"] <> StringValues("chunked") then
                                        contextResponse.Headers.["Transfer-Encoding"] <- StringValues("chunked")
                                else
                                    contextResponse.ContentLength <- Nullable(op.LongLength)
                            with | _ -> () // Content length info is not so important...

                        use tmpOutput = new MemoryStream(op)
                        if tmpOutput.CanSeek then
                            tmpOutput.Seek(0L, SeekOrigin.Begin) |> ignore
                        
                        do! copy3 tmpOutput
                        return ()
                    }
            task {
                use streamWebOutput = contextResponse.Body
                use buffer = new MemoryStream()
                    
                if isCompressable then
                    contextResponse.Body <- buffer // stream
                else
                    ()

                do! next.Invoke()

                let usecompress = isCompressable || checkCompressability (Some buffer)
                if usecompress && checkNoValidETag contextRequest contextResponse cancellationSrc contextResponse.Body then
                    let copy1() =
                        task {
                            do! contextResponse.Body.CopyToAsync(streamWebOutput, defaultBufferSize, cancellationToken)
                            contextResponse.Body <- streamWebOutput
                        } :> Task
                    let copy2 (zipped:Stream) = contextResponse.Body.CopyToAsync(zipped, defaultBufferSize, cancellationToken)
                    let copy3 (zippedData:MemoryStream) =
                        task {
                            do! zippedData.CopyToAsync(streamWebOutput, defaultBufferSize, cancellationToken)
                            contextResponse.Body <- streamWebOutput
                        } :> Task
                    return! continuation2 copy1 copy2 copy3
                else 
                    return ()
            } :> Task

        let compress (context:HttpContext) (settings:CompressionSettings) (mode:ResponseMode) =
            let cancellationSrc = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted)
            let cancellationToken = cancellationSrc.Token

            let encodings = 
                if cancellationToken.IsCancellationRequested then "" 
                else
                    match context.Request.Headers.TryGetValue "Accept-Encoding" with
                    | true, x -> x.ToString()
                    | false, _ -> ""
            let encodeOutput (enc:SupportedEncodings) = 

                match settings.CacheExpireTime with
                | ValueSome d when not (context.Response.Headers.IsReadOnly) -> context.Response.Headers.["Expires"] <- StringValues(d.ToString())
                | _ -> ()

                match mode with
                | File -> encodeFile enc settings context.Request context.Response cancellationSrc
                | ContextResponseBody(next) ->

                    if cancellationToken.IsCancellationRequested then 
                        task {
                            do! next.Invoke()
                            return ()
                        }
                    else
                        encodeStream enc settings context.Request context.Response cancellationSrc next

            let encodeTask() =
                let writeAsyncContext() =
                    match mode with
                    | File ->
                        task {
                            let! comp, r = getFile settings context.Request context.Response cancellationSrc
                            if comp then return! context.Response.Body.WriteAsync(r, 0, r.Length, cancellationToken)
                            else return! Task.Delay 50 
                        } :> Task
                    | ContextResponseBody(next) ->
                        next.Invoke()
                if String.IsNullOrEmpty encodings then writeAsyncContext()
                elif encodings.Contains "deflate" && not(settings.DeflateDisabled) then encodeOutput Deflate
                elif encodings.Contains "gzip" then encodeOutput GZip
                else writeAsyncContext()

            encodeTask

open OwinCompression

[<Extension>]
type CompressionExtensions =

    [<Extension>]
    static member UseCompressionModule(app:IApplicationBuilder, settings:CompressionSettings) =
        app.Use(fun context next ->
            (Internals.compress context settings (ResponseMode.ContextResponseBody next) )()
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
            (Internals.compress context settings ResponseMode.File)() 
        ))

    [<Extension>]
    static member UseCompressionModuleLogTime(app:IApplicationBuilder, settings:CompressionSettings) =
        app.Use(fun context next ->
            task {
                let sw = System.Diagnostics.Stopwatch.StartNew()
                let! r = (Internals.compress context settings (ResponseMode.ContextResponseBody next) )()
                sw.Stop()
                let measure = "Took " + sw.Elapsed.TotalMilliseconds.ToString()
                System.Diagnostics.Debug.WriteLine measure
                return r
            } :> Task
        )

    /// You can set a path that is url that will be captured.
    /// The subsequent url-path will be mapped to server path.
    /// Uses OwinCompression.DefaultCompressionSettings
    [<Extension>]
    static member MapCompressionModule(app:IApplicationBuilder, path:PathString) =
        CompressionExtensions.MapCompressionModule(app, path, DefaultCompressionSettings)
