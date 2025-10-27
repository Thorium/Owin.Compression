(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#I @"./../../packages/Owin/lib/net40"
#r @"nuget: Microsoft.Owin"
#r @"nuget: Microsoft.Owin.Hosting"
#r @"nuget: Microsoft.Owin.Host.HttpListener"
#r @"nuget: Owin.Compression"
#r @"nuget: Microsoft.Owin.StaticFiles"
#r @"nuget: Microsoft.Owin.FileSystems"

(**
Owin.Compression
======================

Owin.Compression (Deflate / GZip / Brotli) module ("middleware") for the Microsoft OWIN pipeline. It can be used with .NET Full, .NET Core, .NET Standard, .NET6.0, and so on. It also works with Selfhost and AspNetCore (e.g. with Kestrel, which is OWIN based server).
It compresses the web request responses to make the transfer smaller, and it supports eTag caching.

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

The default compression used is deflate, then gzip, as deflate should be faster.
Brotli is supported only in .NET Standard 2.1 or higher (e.g., .NET 8.0, .NET 6.0), using ASP.NET Core's built-in BrotliStream support.
This also supports streaming responses. The config allows you to disable deflate and streaming if you prefer.


eTag-caching
----------

1. When the server reads the content before compression, it calculates a hash-code over it.
2. The hash-code is sent as ETag response header to the client with the response
3. The next time the client asks for the same resource, it sends an If-None-Match header in the request with the same value.
4. After the server reads the content before the compression, it calculates a hash-code over it. If it matches the If-None-Match of the request, the server can skip the compression and skip the sending and just send http status code 304 to the client which means "use what you have, it's not modified since".


Example #1
----------

This example demonstrates using MapCompressionModule-function defined in this sample library.

```csharp
	using System;
	using Owin;
	[assembly: Microsoft.Owin.OwinStartup(typeof(MyServer.MyWebStartup))]
	namespace MyServer
	{
		class MyWebStartup
		{
			public void Configuration(Owin.IAppBuilder app)
			{
                // This will compress the whole request, if you want to use e.g. Microsoft.Owin.StaticFiles server:
                // app.UseCompressionModule()

				var settings = OwinCompression.DefaultCompressionSettingsWithPath("c:\\temp\\"); //" server path
				//or var settings = new CompressionSettings( ... )
				app.MapCompressionModule("/zipped", settings);
			}
		}

		class Program
		{
			static void Main(string[] args)
			{
				Microsoft.Owin.Hosting.WebApp.Start<MyWebStartup>("http://*:8080"); //" run on localhost.
				Console.WriteLine("Server started... Press enter to exit.");
				Console.ReadLine();
			}
		}
	}
```

And now your files are smaller than with e.g. just Microsoft.Owin.StaticFiles -library server:

<img src="https://raw.githubusercontent.com/Thorium/Owin.Compression/master/screen.png" alt="compressed" width="1000"/>

Even though the browser sees everything as plain text, the traffic is actually transferred in compressed format.
You can monitor the traffic with e.g. Fiddler.

Example #2
----------

Running on OWIN Self-Host (Microsoft.Owin.Hosting) with static files server (Microsoft.Owin.StaticFiles)
and compressing only the ".json"-responses (and files) on-the-fly, with only gzip and not deflate:

```csharp
	using System;
	using Owin;
	[assembly: Microsoft.Owin.OwinStartup(typeof(MyServer.MyWebStartup))]
	namespace MyServer
	{
		class MyWebStartup
		{
			public void Configuration(Owin.IAppBuilder app)
			{
				var settings = new CompressionSettings(
					serverPath: "",
					allowUnknonwnFiletypes: false,
					allowRootDirectories: false,
					cacheExpireTime: Microsoft.FSharp.Core.FSharpOption<DateTimeOffset>.None,
					allowedExtensionAndMimeTypes:
						new[] { Tuple.Create(".json", "application/json") },
					minimumSizeToCompress: 1000,
					streamingDisabled: false,
					deflateDisabled: true
					);
				app.UseCompressionModule(settings);
			}
		}

		class Program
		{
			static void Main(string[] args)
			{
				Microsoft.Owin.Hosting.WebApp.Start<MyWebStartup>("http://*:8080");
				Console.WriteLine("Server started... Press enter to exit.");
				Console.ReadLine();
			}
		}
	}
```

Example #3
----------

Running on OWIN Self-Host (Microsoft.Owin.Hosting) with static files server (Microsoft.Owin.StaticFiles)
and compressing all the responses (and files) on-the-fly. This example is in F-Sharp (and can be run with F#-interactive):

*)


#r "Owin.dll"
#r "Microsoft.Owin.dll"
#r "Microsoft.Owin.FileSystems.dll"
#r "Microsoft.Owin.Hosting.dll"
#r "Microsoft.Owin.StaticFiles.dll"
#r "System.Configuration.dll"
#r "Owin.Compression.dll"

open Owin
open System

module Examples =

type MyStartup() =
    member __.Configuration(app:Owin.IAppBuilder) =
        let app1 = app.UseCompressionModule()
        app1.UseFileServer "/." |> ignore
        ()

let server = Microsoft.Owin.Hosting.WebApp.Start<MyStartup> "http://*:6000"
Console.WriteLine "Press Enter to stop & quit."
Console.ReadLine() |> ignore
server.Dispose()

(**

Example #4
----------

Running on ASP.NET Core web API on .NET 6.0. You can use C# but this example is in F#
just because of the shorter syntax. The full project is available in tests-folder of this project:

*)

open System
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Owin

module Program =

    [<EntryPoint>]
    let main args =

        let builder = WebApplication.CreateBuilder args
        builder.Services.AddControllers() |> ignore
        let app = builder.Build()

        let compressionSetting =
            {OwinCompression.DefaultCompressionSettings with
                CacheExpireTime = Some (DateTimeOffset.Now.AddDays 7.)
                AllowUnknonwnFiletypes = true
                StreamingDisabled = true
            }
        (app :> IApplicationBuilder).UseCompressionModule(compressionSetting) |> ignore
        app.MapControllers() |> ignore
        app.Run()
        0
(**

https://github.com/Thorium/Owin.Compression/tree/master/tests/Aspnet.Core.WebAPI.Test

Example #5
----------

More complete examples can be found <a href="https://github.com/Thorium/WebsitePlayground">here</a>.


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
consider adding [samples][content] that can be turned into documentation. You might
also want to read the [library design notes][readme] to understand how it works.

The library is available under a Public Domain license, which allows modification and
redistribution for both commercial and non-commercial purposes. For more information see the
[License file][license] in the GitHub repository.

  [content]: https://github.com/fsprojects/Owin.Compression/tree/master/docs/content
  [gh]: https://github.com/fsprojects/Owin.Compression
  [issues]: https://github.com/fsprojects/Owin.Compression/issues
  [readme]: https://github.com/fsprojects/Owin.Compression/blob/master/README.md
  [license]: https://github.com/fsprojects/Owin.Compression/blob/master/LICENSE.txt
*)
