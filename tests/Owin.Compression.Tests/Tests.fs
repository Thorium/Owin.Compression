namespace Owin.Compression.Test

open Owin
open System
open FsUnit.Xunit
open System.Collections.Generic
open System.Threading.Tasks
open Xunit

open Owin
open System

module WebStart =
    type MyWebStartup() =
        member __.Configuration(app:Owin.IAppBuilder) =
            let compressionSetting = 
                {OwinCompression.DefaultCompressionSettings with 
                    CacheExpireTime = Some (DateTimeOffset.Now.AddDays 7.) }
            app.MapCompressionModule("/zipped", compressionSetting) |> ignore 
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
        use server = Microsoft.Owin.Hosting.WebApp.Start<WebStart.MyWebStartup> "http://*:8080"
        System.Threading.Thread.Sleep 3000
        Assert.NotNull server
        
