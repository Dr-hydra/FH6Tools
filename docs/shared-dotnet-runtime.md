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

The script creates two ZIP packages:

- `FH6Tools-<version>-win-x64-with-runtime.zip` for first-time installation.
- `FH6Tools-<version>-win-x64-update.zip` for updating an existing installation.

The full package includes the latest .NET Desktop Runtime 10 and ASP.NET Core
Runtime 10 in the shared `dotnet` directory. The update package omits that
directory and relies on the runtime already present in the target installation.
Package names preserve compatibility with old updaters, which continue to
prefer the full package. FH6Tools 2.0.2 and later prefer the update-only package.

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
