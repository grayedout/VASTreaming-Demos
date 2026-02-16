# VASTreaming Demos

[![NuGet](https://img.shields.io/nuget/v/VAST.Demo.Core)](https://www.nuget.org/packages/VAST.Demo.Core)
[![NuGet Downloads](https://img.shields.io/nuget/dt/VAST.Demo.Core)](https://www.nuget.org/packages/VAST.Demo.Core)
[![.NET](https://img.shields.io/badge/.NET-10.0-blue)](https://dotnet.microsoft.com)
[![.NET Framework](https://img.shields.io/badge/.NET_Framework-4.5.2-blue)](https://dotnet.microsoft.com)
[![.NET Standard](https://img.shields.io/badge/.NET_Standard-2.0-blue)](https://dotnet.microsoft.com)
[![License](https://img.shields.io/badge/license-proprietary-red)](https://www.vastreaming.net)

Sample applications demonstrating the [VASTreaming](https://www.vastreaming.net) media streaming SDK for .NET.

These projects showcase RTSP, RTMP, HLS, DASH, SRT, NDI, WebRTC, and ONVIF capabilities across different .NET application types.

> **Note:** This is the first public release of the demo projects. Bugs are expected, particularly due to assembly obfuscation of the NuGet packages. If you encounter any issues, please [open an issue](https://github.com/grayedout/VASTreaming-Demos/issues) or contact support at support@vastreaming.net.

## Demo Projects

| Project | Description |
|---------|-------------|
| **Demo.Server.NET** | Console-based streaming server (.NET 10.0, Windows, Linux, macOS) |
| **Demo.Server.NET4** | Console-based streaming server (.NET Framework 4.7.2, Windows) |
| **Demo.Server.ASPNETCore** | ASP.NET Core streaming server with web API (.NET 10.0, Windows, Linux) |
| **Demo.Server.Blazor** | Blazor Server streaming application (.NET 10.0, Windows, Linux) |

WebRTC HTML test pages are included in the root folder for browser-based WebRTC testing.

## Getting Started

1. Obtain a free demo license key at [api.vastream.ing](https://api.vastream.ing/).

2. Set your demo license key in the `App` class constructor:

```csharp
VAST.Common.License.Key = "YOUR_DEMO_LICENSE_KEY";
```

3. Build and run one of the demo projects.

For detailed setup instructions and API reference, see the [demo documentation](https://www.vastreaming.net/docs/gettingstarted/sample-applications.html).

## NuGet Packages

| Package | Description |
|---------|-------------|
| **VAST.Demo.Core** | Core managed libraries (required) |
| **VAST.Demo.Ext.Win32** | Windows platform extensions and native dependencies |
| **VAST.Demo.Ext.Linux** | Linux platform extensions and native dependencies |

## Requirements

- .NET 10.0 or .NET Framework 4.7.2+
- Windows x64 or Linux x64

## License

These demo projects and the VASTreaming SDK are proprietary software. All rights reserved by VASTreaming. For evaluation and educational purposes only.

For more information, visit [vastreaming.net](https://www.vastreaming.net).
