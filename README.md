Build

```bash
./build.ps1
```

Installation

```bash
dotnet new tool-manifest
dotnet tool install dotnet-deps --add-source ./artifacts --version 1.0.0
dotnet tool list
dotnet tool uninstall dotnet-deps
```

Usage

```bash
dotnet deps --help
```