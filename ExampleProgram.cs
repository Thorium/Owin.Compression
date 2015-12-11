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
