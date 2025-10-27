### 1.0.50 - October 27 2025
* Brotli support added in NetStandard2.1 and NET6-... via .NET built-in functions.
* Fix to not expect all files are UTF8 encoded

### 1.0.48 - September 08 2025
* Minor performance optimisations
* Dependency updates

### 1.0.47 - December 13 2024
* Nuget package infromation update

### 1.0.46 - June 21 2024
* Performance optimizations when on .NET Standard 2.1 (NET 6.0 / NET 8.0)

### 1.0.45 - April 1 2024
* NuGet Net Framework 4.7.2 dependency corrected to version 4.8

### 1.0.44 - April 1 2024
* More tolerant pipeline config.
* Minor performance improvements.
* Package frameworks to .NET 4.8, .NET Standard 2.0, and 2.1

### 1.0.41 - March 22 2024
* ASP.NET Core side reference updates

### 1.0.40 - March 22 2024
* Reference component updates, PR #13
* Package frameworks to .NET 4.7.2, .NET Standard 2.0, and 2.1

### 1.0.38 - November 1 2023
* Less exception catching on runtime

### 1.0.37 - November 1 2023
* Minor performance improvements
* FSharp.Core update

### 1.0.34 - October 7 2023
* Minor performance improvements

### 1.0.33 - March 7 2023
* Dependency update: Microsoft.Extensions.Primitives

### 1.0.32 - November 23 2022
* Improvements on error handling
* Improvements on picking which files to compress

### 1.0.30 - November 18 2022
* StreamingDisabled setting for disabling HTTP1.1 streaming responses
* Streaming added on static files
* AspNET Core WebAPI .NET Standard fixes (and test project added)

### 1.0.29 - August 11 2022
* Package dependency update

### 1.0.28 - August 10 2022
* VS2022 update
* Fixed task compilation

### 1.0.26 - June 17 2022
* Reference component update
* Better performance via F# 6.0 tasks

### 1.0.24 - March 22 2022
* Fix for avoiding double compression

### 1.0.23 - July 13 2021
* Reference component update

### 1.0.22 - February 01 2021
* Reference component update

### 1.0.21 - July 11 2018
* Reference component update

### 1.0.20 - February 20 2018
* Check for cancellation token before reading headers

### 1.0.19 - February 20 2018
* References updated

### 1.0.18 - November 08 2017
* Fix for stream ETAGs #8

### 1.0.17 - October 02 2017
* Fix for Owin dependency #7
* Initial conversion for .NET Standard 2.0 using Microsoft.AspNetCore.Http, not tested yet.

### 1.0.16 - June 09 2017
* References updated

### 1.0.15 - April 26 2017
* References updated

### 1.0.14 - March 20 2017
* Minor default config update

### 1.0.13 - March 08 2017
* No functionality changes.
* Dependency updated.

### 1.0.12 - September 10 2016
* Respect Pragma no-cache

### 1.0.11 - September 06 2016
* Added compression based on Mime type
* eTag cache: cancel work if can send 304

### 1.0.10 - June 30 2016
* Added setting option DeflateDisabled

### 1.0.9 - June 29 2016
* Added reference to FSharp.Core

### 1.0.8 - April 15 2016
* Added Vary-header part 3.

### 1.0.7 - April 15 2016
* Added Vary-header part 2.

### 1.0.6 - April 15 2016
* Added Vary-header.

### 1.0.5 - April 06 2016
* Don't compress SignalR requests

### 1.0.4 - April 06 2016
* Better handling of canceled request.

### 1.0.3 - April 04 2016
* Better handling of canceled request.

### 1.0.2 - March 11 2016
* Added support for static files with app.UseCompressionModule()

### 1.0.1 - December 11 2015
* Documentation and C# interface improved

### 1.0 - December 11 2015
* Initial release
