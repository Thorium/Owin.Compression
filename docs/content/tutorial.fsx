(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#I @"./../../packages/Owin/lib/net40"
#I @"./../../packages/Microsoft.Owin/lib/net45" 
#I @"./../../packages/Microsoft.Owin.Hosting/lib/net45"
#I @"./../../bin/Owin.Compression"

(**
Introducing your project
========================

Say more

*)
#r "Owin.dll"
#r "Microsoft.Owin.dll"
#r "Microsoft.Owin.Hosting.dll"
#r "System.Configuration.dll"
#r "Owin.Compression.dll"

open Owin
open System

type MyWebStartup() =
    member __.Configuration(app:Owin.IAppBuilder) =
        let compressionSetting = 
            {DefaultCompressionSettings with 
                ServerPath = System.Configuration.ConfigurationManager.AppSettings.["WwwRoot"]; 
                CacheExpireTime = Some (DateTimeOffset.Now.AddDays 7.) }
        app.MapCompressionModule("/zipped", compressionSetting) |> ignore 
        ()

[<assembly: Microsoft.Owin.OwinStartup(typeof<MyWebStartup>)>]
do()

// and then...

Microsoft.Owin.Hosting.WebApp.Start<MyWebStartup> "http://*:8080"

(**
Some more info
*)
