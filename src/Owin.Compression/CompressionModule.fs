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
        CacheExpireTime = None
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
    let DefaultCompressionSettingsWithPath(path) = 
        {DefaultCompressionSettings with 
            ServerPath = path; CacheExpireTime = Some (DateTimeOffset.Now.AddDays 7.) }

    /// Default settings with custom path and cache-time. C#-helper method.
    let DefaultCompressionSettingsWithPathAndCache(path,cachetime) = 
        {DefaultCompressionSettings with ServerPath = path; CacheExpireTime = Some (cachetime) }

    let private defaultBufferSize = 81920

    let internal compress (context:IOwinContext) (settings:CompressionSettings) (mode:ResponseMode) =
        let cancellationSrc = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(context.Request.CallCancelled)
        let cancellationToken = cancellationSrc.Token

        let getMd5Hash (item:Stream) =
            if item.CanRead then
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
                Some res
            else None

        let create304Response() =
            if cancellationSrc<>null then cancellationSrc.Cancel()
            context.Response.StatusCode <- 304
            context.Response.Body.Close()
            context.Response.Body <- Stream.Null
            context.Response.ContentLength <- Nullable()
            false

        let checkNoValidETag (itemToCheck:Stream) =
            if context.Request.Headers.ContainsKey("If-None-Match") && context.Request.Headers.["If-None-Match"] <> null &&
               (not(context.Request.Headers.ContainsKey("Pragma")) || context.Request.Headers.["Pragma"] <> "no-cache") then
                if context.Request.Headers.["If-None-Match"] = context.Response.ETag then
                    create304Response()
                else
                
                match getMd5Hash(itemToCheck) with
                | Some etag ->
                    if context.Request.Headers.["If-None-Match"] = etag then
                        create304Response()
                    else
                        if String.IsNullOrEmpty context.Response.ETag &&
                                not context.Response.Headers.IsReadOnly then
                            context.Response.ETag <- etag
                        true
                | None -> true
            else
                if String.IsNullOrEmpty context.Response.ETag then
                    match getMd5Hash(itemToCheck) with
                    | Some etag when not context.Response.Headers.IsReadOnly ->
                        context.Response.ETag <- etag
                    | _ -> ()
                true

        let getFile() =
            let unpacked :string = 
                    let p = context.Request.Path.ToString()
                    let p2 = match p.StartsWith("/") with true -> p.Substring(1) | false -> p
                    if not(settings.AllowRootDirectories) && p.Contains("..") then failwith "Invalid path"
                    if File.Exists p then failwith "Invalid resource"
                    Path.Combine ([| settings.ServerPath; p2|])

            let extension = if not (unpacked.Contains ".") then "" else unpacked.Substring(unpacked.LastIndexOf ".")
            let typemap = settings.AllowedExtensionAndMimeTypes |> Map.ofSeq

            match typemap.ContainsKey(extension) with
            | true -> context.Response.ContentType <- typemap.[extension]
            | false when settings.AllowUnknonwnFiletypes -> ()
            | _ ->
                context.Response.StatusCode <- 415
                raise (ArgumentException("Invalid resource type", context.Request.Path.ToString()))

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
                        context.Response.Headers.Add("Last-Modified", [|lastmodified|])
                        if checkNoValidETag(strm.BaseStream) then
                            return true, bytes
                        else
                            return false, null
                with
                | :? FileNotFoundException ->
                    context.Response.StatusCode <- 404
                    return true, null
            }

        let encodings = 
            if cancellationToken.IsCancellationRequested then "" 
            else context.Request.Headers.["Accept-Encoding"]
        let encodeOutput (enc:SupportedEncodings) = 

            match settings.CacheExpireTime with
            | Some d -> context.Response.Expires <- Nullable(d)
            | None -> ()

            match mode with
            | File ->
                task {
                    if cancellationToken.IsCancellationRequested then ()
                    use output = new MemoryStream()
                    let! awaited = getFile()
                    let shouldskip, bytes = awaited
                    if(shouldskip) then
                        if bytes <> null then
                            return! context.Response.WriteAsync(bytes, cancellationToken)
                        else
                            return ()
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
                    let! t1 = zipped.WriteAsync(bytes, 0, bytes.Length, cancellationToken)
                    t1 |> ignore
                    zipped.Close()
                    let op = output.ToArray()

                    let canStream = String.Equals(context.Request.Protocol, "HTTP/1.1", StringComparison.Ordinal)

                    let doStream = 
                        if not(cancellationToken.IsCancellationRequested) then
                            try
                                if canStream && (int64 defaultBufferSize) < op.LongLength then
                                    if not(context.Response.Headers.ContainsKey("Transfer-Encoding")) 
                                        || context.Response.Headers.["Transfer-Encoding"] <> "chunked" then
                                        context.Response.Headers.["Transfer-Encoding"] <- "chunked"
                                    true
                                else
                                    context.Response.ContentLength <- Nullable(op.LongLength)
                                    false

                            with | _ -> 
                                false // Content length info is not so important...
                        else false

                    if doStream then
                        return! zipped.CopyToAsync(context.Response.Body, defaultBufferSize, cancellationToken)
                    else
                        return! context.Response.WriteAsync(op, cancellationToken)
                } :> Task

            | ContextResponseBody(next) ->


                let compressableExtension() =
                    match context.Request.Path.ToString() with
                    | null -> true
                    | x when x.Contains(".") -> 
                        let typemap = settings.AllowedExtensionAndMimeTypes |> Map.ofSeq
                        typemap.ContainsKey(x.Substring(x.LastIndexOf "."))
                    | _ -> false

                if cancellationToken.IsCancellationRequested then 
                    task {
                        do! next.Invoke()
                        return ()
                    }
                else

                let compressableExtension = compressableExtension()

                let checkCompressability buffer =
                    let captureResponse() =
                        match buffer with
                        | Some bufferStream ->
                            context.Response.Body <- bufferStream
                        | None -> ()
                    if compressableExtension then // non-stream, but Invoke can change "/" -> "index.html"
                        captureResponse()
                        true
                    elif String.IsNullOrEmpty context.Response.ContentType then
                        if settings.AllowUnknonwnFiletypes then
                            captureResponse()
                            true
                        else false
                    else 
                        let contentType = 
                            // We are not interested of charset, etc:
                            match context.Response.ContentType.Contains(";") with
                            | false -> context.Response.ContentType.ToLower()
                            | true -> context.Response.ContentType.Split(';').[0].ToLower()
                        if settings.AllowedExtensionAndMimeTypes
                                |> Seq.map snd |> Seq.append ["text/html"]
                                |> Seq.contains(contentType) then 
                            captureResponse()
                            true
                        else
                            false

                let isCompressable =
                    (checkCompressability None) && not(context.Request.Path.ToString().Contains("/signalr/"))
                    && context.Response.Body.CanWrite

                let continuation2 (copy1:unit->Task) (copy2:Stream->Task) (copy3:MemoryStream->Task) = 
                    task {

                        let noCompression =
                            (not context.Response.Body.CanSeek) || (not context.Response.Body.CanRead) 
                                || context.Response.Body.Length < settings.MinimumSizeToCompress
                                || (context.Response.Headers.ContainsKey("Content-Encoding") &&
                                    not(String.IsNullOrWhiteSpace(context.Response.Headers.["Content-Encoding"])))

                        match noCompression with
                        | true -> 
                            if context.Response.Body.CanSeek then
                                context.Response.Body.Seek(0L, SeekOrigin.Begin) |> ignore
                            do! copy1()
                            return ()
                        | false -> 

                            let canStream = String.Equals(context.Request.Protocol, "HTTP/1.1", StringComparison.Ordinal) && not settings.StreamingDisabled

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
                            //let! t1 = zipped.WriteAsync(bytes, 0, bytes.Length, cancellationToken)
                            if context.Response.Body.CanSeek then
                                context.Response.Body.Seek(0L, SeekOrigin.Begin) |> ignore

                            do! copy2(zipped)

                            zipped.Close()
                            let op = output.ToArray()

                            if not(cancellationToken.IsCancellationRequested) then
                                try
                                    if canStream && (int64 defaultBufferSize) < op.LongLength then
                                        if not(context.Response.Headers.ContainsKey("Transfer-Encoding")) 
                                            || context.Response.Headers.["Transfer-Encoding"] <> "chunked" then
                                            context.Response.Headers.["Transfer-Encoding"] <- "chunked"
                                    else
                                        context.Response.ContentLength <- Nullable(op.LongLength)
                                with | _ -> () // Content length info is not so important...

                            use tmpOutput = new MemoryStream(op)
                            if tmpOutput.CanSeek then
                                tmpOutput.Seek(0L, SeekOrigin.Begin) |> ignore
                        
                            do! copy3(tmpOutput)
                            return ()

                        }


                task {

                    use streamWebOutput = context.Response.Body
                    use buffer = new MemoryStream()
                    
                    if isCompressable then
                        context.Response.Body <- buffer // stream
                    else
                        ()

                    do! next.Invoke()

                    let usecompress = isCompressable || checkCompressability (Some buffer)
                    if usecompress && checkNoValidETag(context.Response.Body) then

                        let copy1() =
                            task {
                                do! context.Response.Body.CopyToAsync(streamWebOutput, defaultBufferSize, cancellationToken)
                                context.Response.Body <- streamWebOutput
                            } :> Task
                        let copy2 (zipped:Stream) = context.Response.Body.CopyToAsync(zipped, defaultBufferSize, cancellationToken)
                        let copy3 (zippedData:MemoryStream) =
                            task {
                                do! zippedData.CopyToAsync(streamWebOutput, defaultBufferSize, cancellationToken)
                                context.Response.Body <- streamWebOutput
                            } :> Task
                        return! continuation2 copy1 copy2 copy3

                    else 
                        return ()
                } :> Task

        let encodeTask() =
            let WriteAsyncContext() =
                match mode with
                | File ->
                    task {
                        let! comp, r = getFile()
                        if comp then return! context.Response.WriteAsync(r, cancellationToken)
                        else return! Task.Delay 50 
                    } :> Task
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
        ))

    /// You can set a path that is url that will be captured.
    /// The subsequent url-path will be mapped to server path.
    /// Uses OwinCompression.DefaultCompressionSettings
    [<Extension>]
    static member MapCompressionModule(app:IAppBuilder, path:string) =
        CompressionExtensions.MapCompressionModule(app, path, DefaultCompressionSettings)
