
# Owin.Compression

Compression (Deflate / GZip / Brotli) module for Microsoft OWIN Selfhost filesystem pipeline.
It can also be used with AspNetCore, e.g. with .NET8.0 and Kestrel.

With this module, you can compress (deflate, gzip, or brotli) large files (like concatenated *.js or *.css files) to reduce the amount of web traffic.
It supports eTag caching: If the client's sent hashcode is a match, send 302 instead of re-sending the same content.

It also supports streaming responses. The config allows you to disable deflate, brotli, and streaming if you prefer.

**Note:** Brotli compression is available only when targeting .NET Standard 2.1 or higher (e.g., .NET 8.0, .NET 6.0), using ASP.NET Core's built-in BrotliStream support.

This project works on C# and F# and should work on all .NET platforms, also on Windows, and even Mono as well.


Here is a demo in action from Fiddler net traffic monitor:

![compressed](screen.png)

Read the [Getting started tutorial](https://thorium.github.io/Owin.Compression/index.html#Getting-started) to learn more.

Documentation: https://thorium.github.io/Owin.Compression


