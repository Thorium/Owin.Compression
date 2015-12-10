(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#I @"./../../packages/Owin/lib/net40"
#I @"./../../packages/Microsoft.Owin/lib/net45" 
#I @"./../../packages/Microsoft.Owin.Hosting/lib/net45"
#I @"./../../bin/Owin.Compression"

(**
Owin.Compression
======================

Documentation

<div class="row">
  <div class="span1"></div>
  <div class="span6">
    <div class="well well-small" id="nuget">
      The Owin.Compression library can be <a href="https://nuget.org/packages/Owin.Compression">installed from NuGet</a>:
      <pre>PM> Install-Package Owin.Compression</pre>
    </div>
  </div>
  <div class="span1"></div>
</div>

Example
-------

This example demonstrates using a function defined in this sample library.

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

Samples & documentation
-----------------------

The library comes with comprehensible documentation. 
It can include tutorials automatically generated from `*.fsx` files in [the content folder][content]. 
The API reference is automatically generated from Markdown comments in the library implementation.

 * [Tutorial](tutorial.html) contains a further explanation of this sample library.

 * [API Reference](reference/index.html) contains automatically generated documentation for all types, modules
   and functions in the library. This includes additional brief samples on using most of the
   functions.
 
Contributing and copyright
--------------------------

The project is hosted on [GitHub][gh] where you can [report issues][issues], fork 
the project and submit pull requests. If you're adding a new public API, please also 
consider adding [samples][content] that can be turned into a documentation. You might
also want to read the [library design notes][readme] to understand how it works.

The library is available under Public Domain license, which allows modification and 
redistribution for both commercial and non-commercial purposes. For more information see the 
[License file][license] in the GitHub repository. 

  [content]: https://github.com/fsprojects/Owin.Compression/tree/master/docs/content
  [gh]: https://github.com/fsprojects/Owin.Compression
  [issues]: https://github.com/fsprojects/Owin.Compression/issues
  [readme]: https://github.com/fsprojects/Owin.Compression/blob/master/README.md
  [license]: https://github.com/fsprojects/Owin.Compression/blob/master/LICENSE.txt
*)
