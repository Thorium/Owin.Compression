
# Owin.Compression

Compression (Deflate / GZip) module for Microsoft OWIN Selfhost filesystem pipeline.
Can be used also with AspNetCore e.g. with .NET8.0 and Kestrel.

With this module you can compress, deflate / gzip large files (like concatenated *.js or *.css files) to reduce amount of web traffic.
Supports eTag caching: If client sent hashcode match, sends 302 instead of re-sending the same content.

This project works on C# and F# and should work on all .NET platforms, also on Windows, and even Mono as well.


Here is a demo in action from Fiddler net traffic monitor:

![compressed](screen.png)

Read the [Getting started tutorial](https://thorium.github.io/Owin.Compression/index.html#Getting-started) to learn more.

Documentation: https://thorium.github.io/Owin.Compression


