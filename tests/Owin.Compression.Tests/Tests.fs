module Owin.Compression.Tests

open Owin
open System
open NUnit.Framework
open System.Collections.Generic
open System.Threading.Tasks

[<Test>]
let ``safe default settings`` () =
    let settings = OwinCompression.DefaultCompressionSettings
    Assert.AreEqual(false,settings.AllowUnknonwnFiletypes)
    Assert.AreEqual(false,settings.AllowRootDirectories)



open Owin
open System

type MyWebStartup() =
    member __.Configuration(app:Owin.IAppBuilder) =
        let compressionSetting = 
            {OwinCompression.DefaultCompressionSettings with 
                CacheExpireTime = Some (DateTimeOffset.Now.AddDays 7.) }
        app.MapCompressionModule("/zipped", compressionSetting) |> ignore 
        ()

[<assembly: Microsoft.Owin.OwinStartup(typeof<MyWebStartup>)>]
do()

[<Test>]
let ``Server can be started when MapCompressionModule is used`` () =
    use server = Microsoft.Owin.Hosting.WebApp.Start<MyWebStartup> "http://*:8080"
    System.Threading.Thread.Sleep 3000
    Assert.IsNotNull server
