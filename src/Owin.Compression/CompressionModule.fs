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

/// Settings for compression.
type CompressionSettings = {
    ServerPath: string;
    AllowUnknonwnFiletypes: bool;
    AllowRootDirectories: bool;
    CacheExpireTime: DateTimeOffset option;
    AllowedExtensionAndMimeTypes: IEnumerable<string*string>
    }

module OwinCompression =
    /// Default settings for compression.
    let DefaultCompressionSettings = {
        ServerPath = __SOURCE_DIRECTORY__;
        AllowUnknonwnFiletypes = false;
        AllowRootDirectories = false;
        CacheExpireTime = None
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

    let internal compress (context:IOwinContext) (settings:CompressionSettings) =
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

        match settings.CacheExpireTime with
        | Some d -> context.Response.Expires <- Nullable(d)
        | None -> ()

        let lastmodified = File.GetLastWriteTimeUtc(unpacked).ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'", System.Globalization.CultureInfo.InvariantCulture)
        context.Response.Headers.Add("Last-Modified", [|lastmodified|])

        let bytes = File.ReadAllText(unpacked) |> System.Text.Encoding.UTF8.GetBytes

        context.Response.ETag <- (bytes.LongLength.ToString() + lastmodified + unpacked).GetHashCode().ToString()

        let encodings = context.Request.Headers.["Accept-Encoding"]
        let encodeOutput (enc:SupportedEncodings) = 
            async {
                use output = new MemoryStream()
                use zipped = 
                    match enc with
                    | Deflate -> 
                        context.Response.Headers.Add("Content-Encoding", [|"deflate"|])
                        new DeflateStream(output, CompressionMode.Compress) :> Stream
                    | GZip -> 
                        context.Response.Headers.Add("Content-Encoding", [|"gzip"|])
                        new GZipStream(output, CompressionMode.Compress) :> Stream
                let! t1 = zipped.WriteAsync(bytes, 0, bytes.Length, context.Request.CallCancelled) |> awaitTask
                zipped.Close()
                let op = output.ToArray()
                return! context.Response.WriteAsync(op) |> awaitTask
            } |> Async.StartAsTask :> Task

        let encodeTask() =
            if String.IsNullOrEmpty(encodings) then
                context.Response.WriteAsync(bytes)
            elif encodings.Contains "deflate" then encodeOutput Deflate
            elif encodings.Contains "gzip" then encodeOutput GZip
            else context.Response.WriteAsync(bytes)

        encodeTask

open OwinCompression

[<Extension>]
type CompressionExtensions =

    [<Extension>]
    static member UseCompressionModule(app:IAppBuilder, settings:CompressionSettings) =
        app.Use(fun context next ->
            async {
                let! t2 = (compress context settings)() |> awaitTask
                return! next.Invoke() |> awaitTask
            } |> Async.StartAsTask :> Task
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
            (compress context settings)() 
            |> awaitTask |> Async.StartAsTask :> _
        ))

    /// You can set a path that is url that will be captured.
    /// The subsequent url-path will be mapped to server path.
    /// Uses OwinCompression.DefaultCompressionSettings
    [<Extension>]
    static member MapCompressionModule(app:IAppBuilder, path:string) =
        CompressionExtensions.MapCompressionModule(app, path, DefaultCompressionSettings)
