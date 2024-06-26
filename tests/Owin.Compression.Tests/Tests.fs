namespace Owin.Compression.Test

open Owin
open System
open FsUnit.Xunit
open System.Collections.Generic
open System.Threading.Tasks
open Xunit

open Owin
open System
open Microsoft

module MockOwin =
    let generateResponse (contentBody:string option) =
        let mutable etag = ""
        let mutable body =
            match contentBody with
            | Some content -> new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes content) :> System.IO.Stream
            | None -> new System.IO.MemoryStream() :> System.IO.Stream
        let mutable status = 200
        let setBody v =
            body <- v

        let headers = Owin.HeaderDictionary(Dictionary<string, _>())
        { new Microsoft.Owin.IOwinResponse with
              member this.Body 
                with get () = body
                and set v = setBody v
              member this.ContentLength with get () = Nullable(body.Length) and set v = ()
              member this.ContentType with get () = "html" and set v = ()
              member this.Context = raise (System.NotImplementedException())
              member this.Cookies = raise (System.NotImplementedException())
              member this.ETag with get () = etag and set v = etag <- v
              member this.Environment = raise (System.NotImplementedException())
              member this.Expires with get () = Nullable(DateTime.Today.AddMonths 1) and set v = ()
              member this.Get(key) = raise (System.NotImplementedException())
              member this.Headers = headers
              member this.OnSendingHeaders(callback, state) = raise (System.NotImplementedException())
              member this.Protocol with get () = "http" and set v = ()
              member this.ReasonPhrase with get () = "" and set v = ()
              member this.Redirect(location) = raise (System.NotImplementedException())
              member this.Set(key, value) = raise (System.NotImplementedException())
              member this.StatusCode with get () = status and set v = status <- v
              member this.Write(text: string): unit = ()
              member this.Write(data: byte array): unit = ()
              member this.Write(data: byte array, offset: int, count: int): unit = ()
              member this.WriteAsync(text: string): Task = task { return () } :> Task
              member this.WriteAsync(text: string, token: Threading.CancellationToken): Task = task { return () } :> Task 
              member this.WriteAsync(data: byte array): Task = body.WriteAsync(data, 0, data.Length)
              member this.WriteAsync(data: byte array, token: Threading.CancellationToken): Task = body.WriteAsync(data, 0, data.Length)
              member this.WriteAsync(data: byte array, offset: int, count: int, token: Threading.CancellationToken): Task = body.WriteAsync(data, 0, data.Length)
        }
    let generateRequest() =
        let headers = Owin.HeaderDictionary(Dictionary<string, _>())
        headers.Add("Accept-Encoding", [|"gzip"|])
        let mutable path = "/index.html"
        { new Microsoft.Owin.IOwinRequest with
              member this.Accept
                  with get () = raise (System.NotImplementedException())
                  and set v = raise (System.NotImplementedException())
              member this.Body
                  with get () = raise (System.NotImplementedException())
                  and set v = raise (System.NotImplementedException())
              member this.CacheControl
                  with get () = raise (System.NotImplementedException())
                  and set v = raise (System.NotImplementedException())
              member this.CallCancelled
                  with get () = raise (System.NotImplementedException())
                  and set v = raise (System.NotImplementedException())
              member this.ContentType
                  with get () = raise (System.NotImplementedException())
                  and set v = raise (System.NotImplementedException())
              member this.Context = raise (System.NotImplementedException())
              member this.Cookies = raise (System.NotImplementedException())
              member this.Environment = raise (System.NotImplementedException())
              member this.Get(key) = raise (System.NotImplementedException())
              member this.Headers = headers
              member this.Host
                  with get () = raise (System.NotImplementedException())
                  and set v = raise (System.NotImplementedException())
              member this.IsSecure = raise (System.NotImplementedException())
              member this.LocalIpAddress
                  with get () = raise (System.NotImplementedException())
                  and set v = raise (System.NotImplementedException())
              member this.LocalPort
                  with get () = raise (System.NotImplementedException())
                  and set v = raise (System.NotImplementedException())
              member this.MediaType
                  with get () = raise (System.NotImplementedException())
                  and set v = raise (System.NotImplementedException())
              member this.Method
                  with get () = raise (System.NotImplementedException())
                  and set v = raise (System.NotImplementedException())
              member this.Path
                  with get () = Owin.PathString path
                  and set v = path <- v.Value
              member this.PathBase
                  with get () = raise (System.NotImplementedException())
                  and set v = raise (System.NotImplementedException())
              member this.Protocol
                  with get () = "http"
                  and set v = ()
              member this.Query = raise (System.NotImplementedException())
              member this.QueryString
                  with get () = raise (System.NotImplementedException())
                  and set v = raise (System.NotImplementedException())
              member this.ReadFormAsync() = raise (System.NotImplementedException())
              member this.RemoteIpAddress
                  with get () = raise (System.NotImplementedException())
                  and set v = raise (System.NotImplementedException())
              member this.RemotePort
                  with get () = raise (System.NotImplementedException())
                  and set v = raise (System.NotImplementedException())
              member this.Scheme
                  with get () = raise (System.NotImplementedException())
                  and set v = raise (System.NotImplementedException())
              member this.Set(key, value) = raise (System.NotImplementedException())
              member this.Uri = raise (System.NotImplementedException())
              member this.User
                  with get () = raise (System.NotImplementedException())
                  and set v = raise (System.NotImplementedException())
        }

module WebStartFileServer =
    type MyWebStartup() =
        member __.Configuration(app:Owin.IAppBuilder) =
            let compressionSetting = 
                {OwinCompression.DefaultCompressionSettings with 
                    CacheExpireTime = ValueSome (DateTimeOffset.Now.AddDays 7.)
                    }
            app.MapCompressionModule("/zipped", compressionSetting) |> ignore 
            ()

    [<assembly: Microsoft.Owin.OwinStartup(typeof<MyWebStartup>)>]
    do()

module WebStart =
    type MyWebStartup() =
        member __.Configuration(app:Owin.IAppBuilder) =
            let compressionSetting = 
                {OwinCompression.DefaultCompressionSettings with 
                    CacheExpireTime = ValueSome (DateTimeOffset.Now.AddDays 7.)
                    }

            app.UseCompressionModule(compressionSetting) |> ignore
            ()

    [<assembly: Microsoft.Owin.OwinStartup(typeof<MyWebStartup>)>]
    do()

type ``Server fixture`` () =
    [<Fact>]
    member test.``safe default settings`` () =
        let settings = OwinCompression.DefaultCompressionSettings
        settings.AllowUnknonwnFiletypes |> should equal false
        settings.AllowRootDirectories |> should equal false

    [<Fact>]
    member test. ``Server can be started when MapCompressionModule is used`` () =
        // May need admin rights
        use server = Microsoft.Owin.Hosting.WebApp.Start<WebStartFileServer.MyWebStartup> "http://*:8080"
        System.Threading.Thread.Sleep 3000
        // You can uncomment this, debug the test and go to localhost to observe how system works:
        // System.Console.ReadLine() |> ignore
        Assert.NotNull server

    [<Fact>]
    member test. ``Server can be started when UseCompressionModule is used`` () =
        use server = Microsoft.Owin.Hosting.WebApp.Start<WebStart.MyWebStartup> "http://*:8080"
        System.Threading.Thread.Sleep 3000
        Assert.NotNull server

type ``Compress internals fixture`` () =

    [<Fact>]
    member test. ``GetHash should be consistent`` () =
        use ms = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes "hello")
        let h = OwinCompression.Internals.getHash ms
        Assert.Equal(ValueSome "5D41402ABC4B2A76B9719D911017C592", h)

    [<Fact>]
    member test. ``Html should be combressable extension`` () =
        let isC = OwinCompression.Internals.compressableExtension OwinCompression.DefaultCompressionSettings "/file.html"
        Assert.True isC

    [<Fact>]
    member test. ``Set status cached`` () =
        let mockResponse = MockOwin.generateResponse(Some "hello")
        let dne = OwinCompression.Internals.create304Response mockResponse
        Assert.Equal(304, mockResponse.StatusCode)

    [<Fact>]
    member test. ``ETag mismatch test`` () =
        let mockResponse = MockOwin.generateResponse(Some "hello")
        let mockRequest = Owin.OwinRequest()
        mockRequest.Headers.Add("If-None-Match", [|"abc"|])
        let noEtag = OwinCompression.Internals.checkNoValidETag mockRequest mockResponse (new Threading.CancellationTokenSource()) mockResponse.Body
        Assert.NotEqual(304, mockResponse.StatusCode)
        Assert.Equal("5D41402ABC4B2A76B9719D911017C592", mockResponse.ETag)
        Assert.True noEtag

    [<Fact>]
    member test. ``ETag match test`` () =
        let mockResponse = MockOwin.generateResponse(Some "hello")
        let mockRequest = Owin.OwinRequest()
        mockRequest.Headers.Add("If-None-Match", [|"5D41402ABC4B2A76B9719D911017C592"|])
        let noEtag = OwinCompression.Internals.checkNoValidETag mockRequest mockResponse (new Threading.CancellationTokenSource()) mockResponse.Body
        Assert.Equal(304, mockResponse.StatusCode)
        Assert.False noEtag

    [<Fact>]
    member test. ``Compress stream test skips small`` () =
        task {
            let mockResponse = MockOwin.generateResponse(Some "hello")
            let mockRequest = Owin.OwinRequest()
            let taskReturn = Func<Task>(fun _ -> task { return () } :> Task)
            let! res = OwinCompression.Internals.encodeStream SupportedEncodings.Deflate OwinCompression.DefaultCompressionSettings mockRequest mockResponse (new Threading.CancellationTokenSource()) taskReturn
            Assert.NotNull mockResponse.Body
            Assert.Equal(200,mockResponse.StatusCode)
            let content = (mockResponse.Body :?> System.IO.MemoryStream).ToArray() |> System.Text.Encoding.UTF8.GetString
            Assert.Equal("hello",content)
            Assert.False (mockResponse.Headers.ContainsKey "ETag")
            return ()
        } :> Task

    [<Fact>]
    member test. ``Compress stream no-pipeline test`` () =
        task {
            let longstring =  [|1 .. 100_000|] |> Array.map(fun _ -> "x") |> String.concat "abcab460iw3[pn ZWV$dZZo1093ba0v|!!Äcx0c23" 
            let mockResponse = MockOwin.generateResponse (Some longstring)
            
            let mockRequest = MockOwin.generateRequest()
            let taskReturn = Func<Task>(fun _ -> task { return () } :> Task)
            let! isOk = OwinCompression.Internals.encodeStream SupportedEncodings.Deflate OwinCompression.DefaultCompressionSettings mockRequest mockResponse (new Threading.CancellationTokenSource()) taskReturn
            Assert.NotNull mockResponse.Body
            Assert.Equal(200,mockResponse.StatusCode)
            let content = (mockResponse.Body :?> System.IO.MemoryStream).ToArray() |> System.Text.Encoding.UTF8.GetString
            Assert.True(content.Length < longstring.Length, "wasn't compressed")
            Assert.True(content.Length > 0, "Result shouldn't be empty")
            Assert.Equal("3FFF606E12076433E80412E5048FF643", mockResponse.ETag)
            return ()
        } :> Task

    [<Fact>]
    member test. ``Compress stream pipeline test`` () =
        task {
            let longstring =  [|1 .. 100_000|] |> Array.map(fun _ -> "x") |> String.concat "abcab460iw3[pn ZWV$dZZo1093ba0v|!!Äcx0c23" 
            let mockResponse = MockOwin.generateResponse None
            let mockRequest = MockOwin.generateRequest()
            let mutable pipelineProcessing = 0
            let taskReturn = Func<Task>(fun _ ->
                task {
                    mockResponse.Body <- new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes longstring) :> System.IO.Stream
                    pipelineProcessing <- 1
                    return () } :> Task)
            let! isOk = OwinCompression.Internals.encodeStream SupportedEncodings.Deflate OwinCompression.DefaultCompressionSettings mockRequest mockResponse (new Threading.CancellationTokenSource()) taskReturn
            Assert.NotNull mockResponse.Body
            Assert.Equal(200,mockResponse.StatusCode)
            Assert.Equal(1,pipelineProcessing)
            let content = (mockResponse.Body :?> System.IO.MemoryStream).ToArray() |> System.Text.Encoding.UTF8.GetString
            Assert.True(content.Length < longstring.Length, "wasn't compressed")
            Assert.True(content.Length > 0, "Result shouldn't be empty")
            Assert.Equal("3FFF606E12076433E80412E5048FF643", mockResponse.ETag)
            return ()
        } :> Task

    [<Fact>]
    member test. ``Compress file test`` () =
        task {
            let mockResponse = MockOwin.generateResponse None
            let mockRequest = MockOwin.generateRequest()
            mockRequest.Path <- Owin.PathString "/Owin.Compression.DLL"
            let settings = { OwinCompression.DefaultCompressionSettings with AllowUnknonwnFiletypes = true }
            let! isOk = OwinCompression.Internals.encodeFile SupportedEncodings.Deflate settings mockRequest mockResponse (new Threading.CancellationTokenSource()) 
            Assert.NotNull mockResponse.Body
            Assert.Equal(200,mockResponse.StatusCode)
            Assert.NotNull mockResponse.ETag
            let content = (mockResponse.Body :?> System.IO.MemoryStream).ToArray() 
            Assert.True(content.Length > 0)
            
            return ()
        } :> Task
        
(*
open BenchmarkDotNet.Attributes
open BenchmarkDotNet.Running
open BenchmarkDotNet.Jobs

[<SimpleJob (RuntimeMoniker.Net80)>] [<MemoryDiagnoser(true)>]
type Benchmarks() =

    let longstring =  [|1 .. 100_000|] |> Array.map(fun _ -> "x") |> String.concat "abcab460iw3[pn ZWV$dZZo1093ba0v|!!Äcx0c23" |> System.Text.Encoding.UTF8.GetBytes

    [<Benchmark>]
    member this.CompressPipeline () =
        task {
            let mockResponse = MockOwin.generateResponse None
            let mockRequest = MockOwin.generateRequest()
            let taskReturn = Func<Task>(fun _ ->
                task {
                    mockResponse.Body <- new System.IO.MemoryStream(longstring) :> System.IO.Stream
                    return () } :> Task)
            let! isOk = OwinCompression.Internals.encodeStream SupportedEncodings.Deflate OwinCompression.DefaultCompressionSettings mockRequest mockResponse (new Threading.CancellationTokenSource()) taskReturn
            return 1
        }

module BenchmarkTest =
    
    BenchmarkRunner.Run<Benchmarks>(
        BenchmarkDotNet.Configs.ManualConfig
            .Create(BenchmarkDotNet.Configs.DefaultConfig.Instance)
            .WithOptions(BenchmarkDotNet.Configs.ConfigOptions.DisableOptimizationsValidator))
    |> ignore

// To run: Change outpuy type from Library to Exe, then:
// dotnet build --configuration Release
// dotnet run --configuration Release

// .NET 4.8.1
// | Method           | Mean     | Error    | StdDev   | Allocated |
// |----------------- |---------:|---------:|---------:|----------:|
// | CompressPipeline | 17.23 ms | 0.228 ms | 0.213 ms |  77.81 KB |

// .NET 8.0
// | Method           | Mean     | Error     | StdDev    | Gen0   | Allocated |
// |----------------- |---------:|----------:|----------:|-------:|----------:|
// | CompressPipeline | 6.738 ms | 0.0527 ms | 0.0493 ms | 7.8125 |  98.15 KB |

*)
