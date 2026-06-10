# Shared .NET 10 Runtime

FH6Tools and framework-dependent managed tools share one private x64 .NET 10
installation located at:

```text
FH6Tools.exe
dotnet\
  dotnet.exe
  shared\Microsoft.NETCore.App\
  shared\Microsoft.WindowsDesktop.App\
  shared\Microsoft.AspNetCore.App\
```

Create a distributable publish directory with:

```powershell
.\scripts\Publish-FH6Tools.ps1
```

The script publishes FH6Tools as framework-dependent and installs the latest
.NET Desktop Runtime 10 and ASP.NET Core Runtime 10 into the shared `dotnet`
directory. The published FH6Tools apphost searches that relative directory, so
users do not need a system-wide .NET installation.

Every tool process launched by FH6Tools receives `DOTNET_ROOT`,
`DOTNET_ROOT_X64`, `DOTNET_MULTILEVEL_LOOKUP=0`, and a `PATH` beginning with the
shared runtime directory. Native and non-.NET tools ignore these values.

Publish managed tools without their own runtime:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

Keep each tool's `.deps.json` and `.runtimeconfig.json` beside its executable.
All shared tools must target a framework available in the bundled .NET 10
runtime and must use the x64 architecture.
