# Third-party notices

## NetLimiter

NetLimiter is a product and trademark of **Locktime Software s.r.o.** BandBar is an independent, unofficial
project. It is not affiliated with, endorsed by or supported by Locktime Software. A licensed installation of
NetLimiter 5 is required to use it.

BandBar contains **no** NetLimiter code or files, and neither the repository nor the release packages
redistribute any:

- **At run time** BandBar loads the NetLimiter API from the user's own NetLimiter installation, after verifying
  each file's Authenticode signature.
- **At build time** the compiler references the NetLimiter API package published by Locktime Software on nuget.org
  ([NetLimiter](https://www.nuget.org/packages/NetLimiter), MIT License) for type information only. The package is
  downloaded during the build and is not part of the output.

## Microsoft .NET Framework

BandBar runs on the .NET Framework 4.8 that is part of Windows. The build uses Microsoft's
[.NET Framework reference assemblies](https://www.nuget.org/packages/Microsoft.NETFramework.ReferenceAssemblies)
(MIT License) at compile time only.

## Build, test and packaging tools

The installer is created with [Inno Setup](https://jrsoftware.org/isinfo.php) (Inno Setup License). The unit tests
use Microsoft's [MSTest](https://github.com/microsoft/testfx) test framework (MIT License). These tools are used only
to build and test releases and are not included in them.
