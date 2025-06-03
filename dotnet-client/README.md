# vSphere Datastore Information .NET Client

This is a .NET Core implementation of the Go-based VMware vSphere datastore information client.

## Options for Implementation

This directory contains multiple implementations of the vSphere client:

1. **C# with VMware.Vim** - The main implementation, requires VMware SDK
2. **PowerShell with PowerCLI** - A more accessible alternative using PowerShell

## PowerShell Implementation

The PowerShell implementation (`PowerShellImplementation.ps1`) is the easiest way to get started, as it uses VMware PowerCLI which is readily available:

1. Install PowerCLI if you don't have it:
   ```powershell
   Install-Module -Name VMware.PowerCLI -Scope CurrentUser
   ```

2. Run the script:
   ```powershell
   .\PowerShellImplementation.ps1 -Server https://vcenter.example.com -Username administrator@vsphere.local -Password YourPassword
   ```

3. For JSON output:
   ```powershell
   .\PowerShellImplementation.ps1 -Server https://vcenter.example.com -Username user -Password pass -AsJson
   ```

## Features

- Connects to VMware vSphere environments
- Lists clusters, datastore clusters, and datastores with capacity information
- Supports JSON output format similar to the Go version
- Async/await pattern for better performance (C# version)

## C# Implementation Requirements

- .NET 6.0 or higher
- VMware vSphere Management SDK (for the VMware.Vim assembly)
- Newtonsoft.Json NuGet package
- System.CommandLine NuGet package

## Setting up the VMware vSphere SDK

The VMware.Vim library is not available as a standard NuGet package. You need to:

1. Download the VMware vSphere Management SDK from the [VMware Developer Center](https://developer.broadcom.com/sdks/vsphere-management-sdk/latest)
   - You'll need a VMware account (free registration)
   - Look for "VMware vSphere Management SDK" download

2. After downloading, extract the ZIP file and locate the .NET libraries:
   ```
   vsphere-ws/dotnet/cs/lib/
   ```

3. Add references to the following DLLs:
   - VMware.Vim.dll
   - VimService.dll (if required)

4. Alternatively, you can install them to your local .NET assembly cache:
   ```
   gacutil -i VMware.Vim.dll
   gacutil -i VimService.dll
   ```

## Building the C# Version

```bash
cd dotnet-client
dotnet restore
dotnet build
```

## C# Usage

```bash
dotnet run -- --url https://vcenter.example.com/sdk --username administrator@vsphere.local --password YourPassword --datacenter YourDatacenter
```

### Environment Variables

You can also use environment variables instead of command-line arguments:

```bash
export VSPHERE_URL=https://vcenter.example.com/sdk
export VSPHERE_USERNAME=administrator@vsphere.local
export VSPHERE_PASSWORD=YourPassword
export VSPHERE_DATACENTER=YourDatacenter

dotnet run
```

### JSON Output

To get JSON output instead of text:

```bash
dotnet run -- --output json
```

or with the short form:

```bash
dotnet run -- -o json
```

## Command-Line Options

| Option | Short | Description |
|--------|-------|-------------|
| `--url` | `-u` | vSphere URL (can also be set via VSPHERE_URL environment variable) |
| `--username` | `-n` | vSphere username (can also be set via VSPHERE_USERNAME environment variable) |
| `--password` | `-p` | vSphere password (can also be set via VSPHERE_PASSWORD environment variable) |
| `--insecure` | `-k` | Skip verification of server certificate (default: true) |
| `--datacenter` | `-d` | vSphere datacenter name (can also be set via VSPHERE_DATACENTER environment variable) |
| `--output` | `-o` | Output format (json or text, default: text) |

## Example JSON Output

```json
{
  "datacenter": "Datacenter1",
  "clusters": [
    {
      "name": "Cluster1",
      "datastore_clusters": [
        {
          "name": "DatastoreCluster1",
          "datastores": [
            {
              "name": "datastore1",
              "capacity_gb": 1024.0,
              "free_space_gb": 512.0
            },
            {
              "name": "datastore2",
              "capacity_gb": 2048.0,
              "free_space_gb": 1024.0
            }
          ]
        }
      ],
      "standalone_datastores": [
        {
          "name": "standalone1",
          "capacity_gb": 4096.0,
          "free_space_gb": 2048.0
        }
      ]
    }
  ]
}
```

## Alternative Implementations

If you prefer not to use the VMware SDK directly, consider these alternatives:

1. Use PowerCLI (PowerShell module for vSphere) - included in this repository
2. Use [VMware.vSphere.Automation.SDK](https://github.com/vmware/vsphere-automation-sdk-net) instead
3. For a lighter approach, implement a REST client using the vSphere REST API 