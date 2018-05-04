(*** hide ***)
// This block of code is omitted in the generated HTML documentation. Use 
// it to define helpers that you do not want to show in the documentation.
#I "../../bin"
#I @"./../../packages/Owin/lib/net40"
#I @"./../../packages/Microsoft.Owin/lib/net451" 
#I @"./../../packages/Microsoft.Owin.Hosting/lib/net451"
#I @"./../../packages/Microsoft.Owin.Host.HttpListener/lib/net451"
#I @"./../../bin/Owin.Compression"

(**
# Using this library (C-Sharp) #

Create new C# console application project (.NET 4.5 or more). Add reference to NuGet-packages:

- Microsoft.Owin
- Microsoft.Owin.Hosting
- Microsoft.Owin.Host.HttpListener
- Owin.Compression (this package)

Then write the program, e.g.:

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
				var settings = OwinCompression.DefaultCompressionSettingsWithPath(@"c:\temp\");
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
```

Have a large text file in your temp-folder, c:\temp\test\mytempfile.txt

Now, run the program (F5) and start a browser to address:

http://localhost:8080/zipped/test/mytempfile.txt

Observe that the file is transfered as compressed but the browser will automatically decompress the traffic.



### Corresponding code with F-Sharp ###

*)
#r "Owin.dll"
#r "Microsoft.Owin.dll"
#r "Microsoft.Owin.Hosting.dll"
#r "System.Configuration.dll"
#r "Owin.Compression.dll"

open Owin
open System

let serverPath = System.Configuration.ConfigurationManager.AppSettings.["WwwRoot"]

type MyWebStartup() =
    member __.Configuration(app:Owin.IAppBuilder) =
        let compressionSetting = 
            {OwinCompression.DefaultCompressionSettings with 
                ServerPath = serverPath; 
                CacheExpireTime = Some (DateTimeOffset.Now.AddDays 7.) }
        app.MapCompressionModule("/zipped", compressionSetting) |> ignore 
        ()

[<assembly: Microsoft.Owin.OwinStartup(typeof<MyWebStartup>)>]
do()

// and then...

Microsoft.Owin.Hosting.WebApp.Start<MyWebStartup> "http://*:8080"

(**
Or you can use app.UseCompressionModule() in the beginning of the configuration to compress the whole response.
*)

type MyWebStartupExample2() =
    member __.Configuration(app:Owin.IAppBuilder) =
        app.UseCompressionModule() |> ignore
        
        //app.MapSignalR(hubConfig)
        //app.UseFileServer(fileServerOptions) |> ignore
        //etc...

        ()