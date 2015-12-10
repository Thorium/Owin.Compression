module Owin.Compression.Tests

open Owin
open System
open NUnit.Framework
open System.Collections.Generic
open System.Threading.Tasks

[<Test>]
let ``safe default settings`` () =
    let settings = DefaultCompressionSettings
    Assert.AreEqual(false,settings.AllowUnknonwnFiletypes)
    Assert.AreEqual(false,settings.AllowRootDirectories)

//[<Test>]
//let ``IAppBuilder has MapCompressionModule`` () =
//    let app = { new IAppBuilder with 
//         member x.Build (y) = Task.Yield |> box
//         member x.New() = x
//         member x.Properties = Dictionary<string,obj>() :> _
//         member x.Use(a,b) = x
//    }
//    app.MapCompressionModule("/zipped") |> ignore 
//    Assert.IsNotNull app
