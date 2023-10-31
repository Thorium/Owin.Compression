namespace Aspnet.Core.WebAPI.Test
#nowarn "20"
open System
open System.Collections.Generic
open System.IO
open System.Linq
open System.Threading.Tasks
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.HttpsPolicy
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Owin

module Program =
    let exitCode = 0

    [<EntryPoint>]
    let main args =

        let builder = WebApplication.CreateBuilder(args)

        builder.Services.AddControllers()

        let app = builder.Build()

        let compressionSetting = 
            {OwinCompression.DefaultCompressionSettings with 
                CacheExpireTime = ValueSome (DateTimeOffset.Now.AddDays 7.)
                AllowUnknonwnFiletypes = true
                StreamingDisabled = true
                MinimumSizeToCompress = 0
            }

        (app :> IApplicationBuilder).UseCompressionModule(compressionSetting) |> ignore

        app.MapControllers()
        
        app.Run()

        exitCode
