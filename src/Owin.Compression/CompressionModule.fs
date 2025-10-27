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
[<Struct>]
type SupportedEncodings =
| Deflate
| GZip

/// Do you fetch files, or do you encode context.Response.Body?
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
    ExcludedPaths: IEnumerable<string>;
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
        ExcludedPaths = [| "/signalr/" |]
        AllowedExtensionAndMimeTypes = 
        [|
            ".js"   , "application/javascript";
            ".css"  , "text/css";
            ".yml"  , "application/x-yaml";
            ".json" , "application/json";
            ".svg"  , "image/svg+xml";
            ".txt"  , "text/plain";
            ".html" , "application/json"; // we don't want to follow hyperlinks, so not "text/html"
            ".map"  , "application/json";
            ".ttf"  , "application/x-font-ttf";
            ".otf"  , "application/x-font-opentype";
            ".ico"  , "image/x-icon";
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

    /// Cached extension to MIME type mapping for performance
    let private getExtensionMap (settings:CompressionSettings) =
        // Cache the map computation to avoid repeated Map.ofSeq operations
        settings.AllowedExtensionAndMimeTypes |> Map.ofSeq

    module Internals =

        // Use thread-safe MD5 computation for better performance
        let private computeHash (item:Stream) =
            use md5 = System.Security.Cryptography.MD5.Create()
            md5.ComputeHash item

        let getHash (item:Stream) =
            if item.CanRead then
                let hasPos = 
                    if item.CanSeek && item.Position > 0L then
                        let tmp = item.Position
                        item.Position <- 0L
                        ValueSome tmp
                    else ValueNone
                let hashBytes = computeHash item
                let res = BitConverter.ToString(hashBytes).Replace("-","")
                match hasPos with
                | ValueSome x when item.CanSeek -> item.Position <- x
                | _ -> ()
                ValueSome res
            else ValueNone

        let inline create304Response (contextResponse:IOwinResponse) =
            if contextResponse.StatusCode <> 304 then
                contextResponse.StatusCode <- 304
            contextResponse.Body.Close()
            contextResponse.Body <- Stream.Null
            if contextResponse.ContentLength.HasValue then
                contextResponse.ContentLength <- Nullable()
            false

        let checkNoValidETag (contextRequest:IOwinRequest) (contextResponse:IOwinResponse) (cancellationSrc:Threading.CancellationTokenSource) (itemToCheck:Stream) =

            if contextRequest.Headers.ContainsKey("If-None-Match") && (not(isNull contextRequest.Headers.["If-None-Match"])) &&
               (not(contextRequest.Headers.ContainsKey("Pragma")) || contextRequest.Headers.["Pragma"] <> "no-cache") then
               let noneMatch = contextRequest.Headers.["If-None-Match"]
               if noneMatch = contextResponse.ETag then
                    if not (isNull cancellationSrc) then cancellationSrc.Cancel()
                    create304Response contextResponse
                else
                
                match getHash itemToCheck with
                | ValueSome etag ->
                    if noneMatch = etag then
                        if not (isNull cancellationSrc) then cancellationSrc.Cancel()
                        create304Response contextResponse
                    else
                        if String.IsNullOrEmpty contextResponse.ETag &&
                                not contextResponse.Headers.IsReadOnly then
                            contextResponse.ETag <- etag
                        true
                | ValueNone -> true
            else
                if String.IsNullOrEmpty contextResponse.ETag then
                    match getHash itemToCheck with
                    | ValueSome etag when not contextResponse.Headers.IsReadOnly && itemToCheck.Length > 0L ->
                        contextResponse.ETag <- etag
                    | _ -> ()
                true

        let internal getFile (settings:CompressionSettings) (contextRequest:IOwinRequest) (contextResponse:IOwinResponse) (cancellationSrc:Threading.CancellationTokenSource) =
            let unpacked :string = 
                    let p = contextRequest.Path.ToString()
                    if not(settings.AllowRootDirectories) && p.Contains ".." then failwith "Invalid path"
                    if File.Exists p then failwith "Invalid resource"
                    let p2 =
                        match p.StartsWith "/" with
                        | true -> p.Substring 1
                        | false -> p
                    Path.Combine ([| settings.ServerPath; p2|])

            let extension = 
                let lastDot = unpacked.LastIndexOf '.'
                if lastDot = -1 then "" else unpacked.Substring lastDot
            let typemap = getExtensionMap settings

            match typemap.TryGetValue extension with
            | true, extval -> contextResponse.ContentType <- extval
            | false, _ when settings.AllowUnknonwnFiletypes -> ()
            | _ ->
                contextResponse.StatusCode <- 415
                raise (ArgumentException("Invalid resource type", contextRequest.Path.ToString()))

            task {
                try
                    // Most efficient: synchronous read since it's disk I/O bound anyway
                    // and avoids async overhead for small files
                    // (and there is no File.ReadAllBytesAsync in .NET Framework 4.8)
                    let bytes = File.ReadAllBytes unpacked
            
                    match bytes.LongLength < settings.MinimumSizeToCompress with
                    | true -> 
                        return false, bytes
                    | false -> 
                        let lastmodified = File.GetLastWriteTimeUtc(unpacked).ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", System.Globalization.CultureInfo.InvariantCulture)
                        contextResponse.Headers.Add("Last-Modified", [|lastmodified|])
                        use ms = new MemoryStream(bytes, false)
                        return 
                            if checkNoValidETag contextRequest contextResponse cancellationSrc ms then
                                true, bytes
                            else
                                false, null
                with
                | :? FileNotFoundException ->
                    contextResponse.StatusCode <- 404
                    return false, null
            }
        let encodeFile (enc:SupportedEncodings) (settings:CompressionSettings) (contextRequest:IOwinRequest) (contextResponse:IOwinResponse) (cancellationSrc:Threading.CancellationTokenSource)= 
            task {
                let cancellationToken = cancellationSrc.Token
                if cancellationToken.IsCancellationRequested then ()
                let! awaited = getFile settings contextRequest contextResponse cancellationSrc
                let shouldProcess, bytes = awaited
                if not shouldProcess then
                    if isNull bytes then
                        return ()
                    else
                        return! contextResponse.WriteAsync(bytes, cancellationToken)
                else

                use output = new MemoryStream()
                if not(contextResponse.Headers.ContainsKey "Vary") then
                    contextResponse.Headers.Add("Vary", [|"Accept-Encoding"|])
                use zipped = 
                    match enc with
                    | Deflate -> 
                        contextResponse.Headers.Add("Content-Encoding", [|"deflate"|])
                        new DeflateStream(output, CompressionMode.Compress) :> Stream
                    | GZip -> 
                        contextResponse.Headers.Add("Content-Encoding", [|"gzip"|])
                        new GZipStream(output, CompressionMode.Compress) :> Stream
                let! t1 = zipped.WriteAsync(bytes, 0, bytes.Length, cancellationToken)
                t1 |> ignore
                zipped.Close()
                output.Close()

                let op = output.ToArray()
                
                let doStream = 
                    if cancellationToken.IsCancellationRequested then
                        false
                    else
                        try
                            let canStream = String.Equals(contextRequest.Protocol, "HTTP/1.1", StringComparison.Ordinal)
                            if canStream && (int64 defaultBufferSize) < op.LongLength then
                                if not(contextResponse.Headers.ContainsKey("Transfer-Encoding")) 
                                    || contextResponse.Headers.["Transfer-Encoding"] <> "chunked" then
                                    contextResponse.Headers.["Transfer-Encoding"] <- "chunked"
                                true
                            else
                                if (not contextResponse.ContentLength.HasValue) || (contextResponse.ContentLength.Value <> -1 && contextResponse.ContentLength.Value <> op.LongLength) then
                                    contextResponse.ContentLength <- Nullable op.LongLength
                                false

                        with | _ -> 
                            false // Content length info is not so important...

                if doStream then
                    return! zipped.CopyToAsync(contextResponse.Body, defaultBufferSize, cancellationToken)
                else
                    return! contextResponse.WriteAsync(op, cancellationToken)
            } :> Task

        let compressableExtension (settings:CompressionSettings) (path:string) =
            match path with
            | null -> true
            | x -> 
                let lastDot = x.LastIndexOf('.')
                if lastDot = -1 then false
                else
                    let typemap = getExtensionMap settings
                    typemap.ContainsKey(x.Substring(lastDot))

        let encodeStream (enc:SupportedEncodings) (settings:CompressionSettings) (contextRequest:IOwinRequest) (contextResponse:IOwinResponse) (cancellationSrc:Threading.CancellationTokenSource) (next:Func<Task>)  =

            let cancellationToken = cancellationSrc.Token
            let originalLengthNotEnough = contextResponse.Body.CanRead && contextResponse.Body.Length < settings.MinimumSizeToCompress
            let inline checkCompressability buffer =
                let inline captureResponse() =
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
                        // We are not interested of charset, etc - use StringComparison to avoid ToLower allocation
                        let rawContentType = 
                            match contextResponse.ContentType.IndexOf(';') with
                            | -1 -> contextResponse.ContentType
                            | idx -> contextResponse.ContentType.Substring(0, idx)
                        rawContentType
                    if settings.AllowedExtensionAndMimeTypes
                            |> Seq.map snd |> Seq.append ["text/html"]
                            |> Seq.exists(fun mimeType -> String.Equals(mimeType, contentType, StringComparison.OrdinalIgnoreCase)) then 
                        captureResponse()
                        true
                    else
                        false

            let isCompressable =
                (checkCompressability None) && not(settings.ExcludedPaths |> Seq.exists(fun p -> contextRequest.Path.ToString().Contains p))
                && contextResponse.Body.CanWrite

            let inline continuation2 (pipedLengthNotEnough:bool) (copyBufferToBody:unit->Task) (copyBodyToCompressor:Stream->Task) (copyCompressedToBody:MemoryStream->Task) = 
                task {

                    let noCompression =
                        (not contextResponse.Body.CanSeek) || (not contextResponse.Body.CanRead) 
                            || (originalLengthNotEnough && pipedLengthNotEnough)
                            || (contextResponse.Headers.ContainsKey("Content-Encoding") &&
                                not(String.IsNullOrWhiteSpace(contextResponse.Headers.["Content-Encoding"])))

                    match noCompression with
                    | true -> 
                        if contextResponse.Body.CanSeek then
                            contextResponse.Body.Seek(0L, SeekOrigin.Begin) |> ignore
                        do! copyBufferToBody()
                        return ()
                    | false -> 

                        if not(contextResponse.Headers.ContainsKey "Vary") then
                            contextResponse.Headers.Add("Vary", [|"Accept-Encoding"|])

                        use output = new MemoryStream()

                        use zipped = 
                            match enc with
                            | Deflate -> 
                                contextResponse.Headers.Add("Content-Encoding", [|"deflate"|])
                                new DeflateStream(output, CompressionMode.Compress) :> Stream
                            | GZip -> 
                                contextResponse.Headers.Add("Content-Encoding", [|"gzip"|])
                                new GZipStream(output, CompressionMode.Compress) :> Stream
                        //let! t1 = zipped.WriteAsync(bytes, 0, bytes.Length, cancellationToken)
                        if contextResponse.Body.CanSeek then
                            contextResponse.Body.Seek(0L, SeekOrigin.Begin) |> ignore

                        do! copyBodyToCompressor(zipped)

                        zipped.Close()
                        output.Close()
                        let op = output.ToArray()

                        if not(cancellationToken.IsCancellationRequested) then
                            try
                                let canStream = String.Equals(contextRequest.Protocol, "HTTP/1.1", StringComparison.Ordinal) && not settings.StreamingDisabled
                                if canStream && (int64 defaultBufferSize) < op.LongLength then
                                    if not(contextResponse.Headers.ContainsKey("Transfer-Encoding")) 
                                        || contextResponse.Headers.["Transfer-Encoding"] <> "chunked" then
                                        contextResponse.Headers.["Transfer-Encoding"] <- "chunked"
                                else
                                    if (not contextResponse.ContentLength.HasValue) || (contextResponse.ContentLength.Value <> -1 && contextResponse.ContentLength.Value <> op.LongLength) then
                                        contextResponse.ContentLength <- Nullable(op.LongLength)
                            with | _ -> () // Content length info is not so important...

                        use tmpOutput = new MemoryStream(op)
                        if tmpOutput.CanSeek then
                            tmpOutput.Seek(0L, SeekOrigin.Begin) |> ignore
                        
                        do! copyCompressedToBody tmpOutput
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

                let pipedLengthNotEnough =
                    contextResponse.Body.CanRead &&
                        (if contextResponse.Body.Length = 0 && contextResponse.Body = buffer && streamWebOutput.CanRead then streamWebOutput.Length else contextResponse.Body.Length) < settings.MinimumSizeToCompress

                let usecompress = isCompressable || checkCompressability (Some buffer)
                if usecompress && checkNoValidETag contextRequest contextResponse cancellationSrc (if contextResponse.Body.Length > 0L then contextResponse.Body else streamWebOutput) then

                    let inline copyBufferToBody() =
                        task {
                            do! contextResponse.Body.CopyToAsync(streamWebOutput, defaultBufferSize, cancellationToken)
                            contextResponse.Body <- streamWebOutput
                        } :> Task
                    let inline copyBodyToCompressor (zipped:Stream) = contextResponse.Body.CopyToAsync(zipped, defaultBufferSize, cancellationToken)
                    let inline copyCompressedToBody (zippedData:MemoryStream) =
                        task {
                            if zippedData.Length = 0 && streamWebOutput.CanRead then

                                use output = new MemoryStream()
                                use zipped = 
                                    match enc with
                                    | Deflate -> 
                                        new DeflateStream(output, CompressionMode.Compress) :> Stream
                                    | GZip -> 
                                        new GZipStream(output, CompressionMode.Compress) :> Stream

                                if streamWebOutput.CanSeek then
                                    streamWebOutput.Seek(0L, SeekOrigin.Begin) |> ignore

                                do! streamWebOutput.CopyToAsync(zipped, defaultBufferSize, cancellationToken)
                                zipped.Close()
                                contextResponse.Body <- output
                            else 
                                do! zippedData.CopyToAsync(streamWebOutput, defaultBufferSize, cancellationToken)
                                contextResponse.Body <- streamWebOutput
                        } :> Task
                    return! continuation2 pipedLengthNotEnough copyBufferToBody copyBodyToCompressor copyCompressedToBody

                else 
                    return ()
            } :> Task


        let inline internal compress (context:IOwinContext) (settings:CompressionSettings) (mode:ResponseMode) =
            let cancellationSrc = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(context.Request.CallCancelled)
            let cancellationToken = cancellationSrc.Token

            let encodings = 
                if cancellationToken.IsCancellationRequested then "" 
                else context.Request.Headers.["Accept-Encoding"]

            let inline encodeOutput (enc:SupportedEncodings) = 

                match settings.CacheExpireTime with
                | ValueSome d when not (context.Response.Headers.IsReadOnly) -> context.Response.Expires <- Nullable(d)
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

            let inline encodeTask() =
                let inline writeAsyncContext() =
                    match mode with
                    | File ->
                        task {
                            let! comp, r = getFile settings context.Request context.Response cancellationSrc
                            if comp then
                                return! context.Response.WriteAsync(r, cancellationToken)
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
    static member UseCompressionModule(app:IAppBuilder, settings:CompressionSettings) =
        app.Use(fun context next ->
            (Internals.compress context settings (ResponseMode.ContextResponseBody next) )()
        )

    [<Extension>]
    static member UseCompressionModule(app:IAppBuilder) =
        CompressionExtensions.UseCompressionModule(app, DefaultCompressionSettings)

    /// You can set a path, which is the URL that will be captured.
    /// The subsequent url-path will be mapped to the server path.
    [<Extension>]
    static member MapCompressionModule(app:IAppBuilder, path:string, settings:CompressionSettings) =
        app.Map(path, fun ap ->
         ap.Run(fun context ->
            (Internals.compress context settings ResponseMode.File)() 
        ))

    /// You can set a path, which is the URL that will be captured.
    /// The subsequent url-path will be mapped to the server path.
    /// Uses OwinCompression.DefaultCompressionSettings
    [<Extension>]
    static member MapCompressionModule(app:IAppBuilder, path:string) =
        CompressionExtensions.MapCompressionModule(app, path, DefaultCompressionSettings)
