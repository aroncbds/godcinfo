# VMware Datastore Cluster Information Tool

This tool uses the govmomi library to list available datastore clusters with their datastores per VMware cluster. It provides a comprehensive view of your VMware storage organization.

## Features

- Lists all VMware clusters in your environment
- For each cluster, shows all datastore clusters (storage pods) and their datastores
- Lists standalone datastores (not in any datastore cluster)
- Shows capacity and free space information for each datastore

## Requirements

- Go 1.20 or higher
- Access to a VMware vSphere environment
- Credentials with permissions to view datastores and clusters

## Installation

1. Clone the repository:
   ```
   git clone https://github.com/kelaro/godcinfo.git
   cd godcinfo
   ```

2. Install dependencies:
   ```
   make deps
   ```

## Usage

You can run the tool in two ways:

### Using environment variables

```bash
export VSPHERE_URL="https://vcenter.example.com/sdk"
export VSPHERE_USERNAME="admin"
export VSPHERE_PASSWORD="password"
export VSPHERE_DATACENTER="your-datacenter-name"
make run
```

### Using command-line flags

```bash
make build
./godcinfo -url="https://vcenter.example.com/sdk" -username="admin" -password="password" -datacenter="your-datacenter-name"
```

Or use the provided make target:

```bash
VSPHERE_URL="https://vcenter.example.com/sdk" VSPHERE_USERNAME="admin" VSPHERE_PASSWORD="password" VSPHERE_DATACENTER="your-datacenter-name" make run-with-env
```

### Available command-line flags

- `-url`: vSphere URL (required)
- `-username`: vSphere username (required)
- `-password`: vSphere password (required)
- `-datacenter`: vSphere datacenter name (if not specified, the tool will list available datacenters)
- `-insecure`: Skip verification of server certificate (default: true)

### Handling Special Characters in Passwords

If your password contains special characters like `!`, `$`, `&`, etc., you can use one of these methods:

1. Use single quotes to wrap the password:
   ```bash
   ./godcinfo -url="https://vcenter.example.com/sdk" -username="admin" -password='P@ssw0rd!'
   ```

2. Escape special characters with a backslash:
   ```bash
   ./godcinfo -url="https://vcenter.example.com/sdk" -username="admin" -password="P@ssw0rd\!"
   ```

3. Use environment variables (recommended for security):
   ```bash
   export VSPHERE_PASSWORD="P@ssw0rd!"
   ./godcinfo -url="https://vcenter.example.com/sdk" -username="admin"
   ```

## Example Output

```
Using datacenter: DC01
Cluster: Cluster01
-----------------
  Datastore Cluster: StoragePod01
    - Datastore01 (Capacity: 2048.00 GB, Free: 1024.00 GB)
    - Datastore02 (Capacity: 4096.00 GB, Free: 2048.00 GB)
  
  Datastore Cluster: StoragePod02
    - Datastore03 (Capacity: 1024.00 GB, Free: 512.00 GB)
  
  Standalone Datastores:
    - Datastore04 (Capacity: 8192.00 GB, Free: 4096.00 GB)

Cluster: Cluster02
-----------------
  No datastore clusters found for this cluster
  
  Standalone Datastores:
    - Datastore05 (Capacity: 2048.00 GB, Free: 1024.00 GB)
```

## Building

To build the application:

```bash
make build
```

## Makefile Targets

- `make build` - Build the application
- `make run` - Run the application (using environment variables or flags)
- `make run-with-env` - Run with environment variables
- `make clean` - Remove build artifacts
- `make tidy` - Update go.mod dependencies
- `make deps` - Download dependencies
- `make test` - Run tests
- `make help` - Show help information 