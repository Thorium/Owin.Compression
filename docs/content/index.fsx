(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#I @"./../../packages/Owin/lib/net40"
#I @"./../../packages/Microsoft.Owin/lib/net45" 
#I @"./../../packages/Microsoft.Owin.Hosting/lib/net45"
#I @"./../../packages/Microsoft.Owin.Host.HttpListener/lib/net45"
#I @"./../../bin/Owin.Compression"
#I @"./../../packages/Microsoft.Owin.StaticFiles/lib/net45"
#I @"./../../packages/Microsoft.Owin.FileSystems/lib/net45"

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

Default compression used is deflate, then gzip, as deflate should be faster.

Example #1
----------

This example demonstrates using MapCompressionModule-function defined in this sample library.

    [lang=csharp]
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

                var serverpath = "c:\\temp\\";
				var settings = OwinCompression.DefaultCompressionSettingsWithPath(serverpath);
				//or var settings = new CompressionSettings( ... )
				app.MapCompressionModule("/zipped", settings);
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

And now your files are smaller than with e.g. just Microsoft.Owin.StaticFiles -library server:

![compressed](https://raw.githubusercontent.com/Thorium/Owin.Compression/master/screen.png)

Even though the browser sees everything as plain text, the traffic is actually transfered as compressed format.
You can monitor the traffic with e.g. Fiddler.

Example #2
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

More complete example can be found <a href="https://github.com/Thorium/WebsitePlayground">here</a>.


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
