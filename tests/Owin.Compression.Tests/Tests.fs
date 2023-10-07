namespace Owin.Compression.Test

open Owin
open System
open FsUnit.Xunit
open System.Collections.Generic
open System.Threading.Tasks
open Xunit

open Owin
open System

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
        

