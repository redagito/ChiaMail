# ChiaMail

A portable WPF desktop mass mailer for Gmail. Sends personalized bulk emails from a CSV file with placeholder replacement, inline logo, attachments, and configurable rate-limiting.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- Windows (WPF dependency)
- A Gmail account with [2-Step Verification](https://myaccount.google.com/security) enabled
- A [Gmail App Password](https://myaccount.google.com/apppasswords) (16 characters)

## Quick Start

1. Build: `dotnet build ChiaMail.slnx`
2. Run: `dotnet run --project src\ChiaMail`
3. Enter your Gmail address and App Password
4. Load a CSV (see `src\ChiaMail\sample.csv` for format)
5. Write your subject/body with `{FirstName}`, `{LastName}` placeholders
6. Optionally add a logo and attachments
7. Click **Send All Emails**

## Build

```cmd
dotnet build ChiaMail.slnx
```

Output: `src\ChiaMail\bin\Debug\net10.0-windows\`

## Test

```cmd
dotnet test ChiaMail.slnx
```

## Publish (Self-Contained EXE)

Produces a single `ChiaMail.exe` that runs on any Windows x64 machine **without the .NET runtime installed**.

```cmd
dotnet publish src\ChiaMail\ChiaMail.csproj ^
    -c Release -r win-x64 --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -p:DebugType=embedded ^
    -o src\ChiaMail\publish
```

Output: `src\ChiaMail\publish\ChiaMail.exe` + `docs\` folder