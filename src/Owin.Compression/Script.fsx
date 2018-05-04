// Learn more about F# at http://fsharp.org. See the 'F# Tutorial' project
// for more guidance on F# programming.
#I "../../bin"
#I @"./../../packages/Owin/lib/net40"
#I @"./../../packages/Microsoft.Owin/lib/net451" 
#I @"./../../packages/Microsoft.Owin.Hosting/lib/net451"
#I @"./../../bin/Owin.Compression"

#r "Owin.dll"
#r "Microsoft.Owin.dll"
#r "Microsoft.Owin.Hosting.dll"
#r "System.Configuration.dll"
#r "Owin.Compression.dll"

open System

#load "CompressionModule.fs"
open Owin

printfn "%s" (OwinCompression.DefaultCompressionSettings.ToString())
