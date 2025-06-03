# PowerShell implementation of godcinfo using PowerCLI
# This script provides similar functionality to the Go version, with JSON output capability

[CmdletBinding()]
param(
    [Parameter()]
    [string]$Server = $env:VSPHERE_URL,
    
    [Parameter()]
    [string]$Username = $env:VSPHERE_USERNAME,
    
    [Parameter()]
    [string]$Password = $env:VSPHERE_PASSWORD,
    
    [Parameter()]
    [string]$Datacenter = $env:VSPHERE_DATACENTER,
    
    [Parameter()]
    [switch]$AsJson,
    
    [Parameter()]
    [switch]$SkipCertificateCheck = $true
)

# Check if PowerCLI is installed, if not suggest installing it
if (-not (Get-Module -ListAvailable -Name VMware.PowerCLI)) {
    Write-Host "PowerCLI module not found. Please install it with:" -ForegroundColor Yellow
    Write-Host "Install-Module -Name VMware.PowerCLI -Scope CurrentUser" -ForegroundColor Cyan
    exit 1
}

# Import the PowerCLI module
Import-Module VMware.PowerCLI

# Set certificate policy if needed
if ($SkipCertificateCheck) {
    Set-PowerCLIConfiguration -InvalidCertificateAction Ignore -Confirm:$false | Out-Null
}

# Check for required parameters
if (-not $Server -or -not $Username -or -not $Password) {
    Write-Host "Error: Server, Username, and Password are required." -ForegroundColor Red
    Write-Host "Usage: .\PowerShellImplementation.ps1 -Server <url> -Username <user> -Password <pass> [-Datacenter <name>] [-AsJson]" -ForegroundColor Yellow
    exit 1
}

# Function to convert sizes to GB
function ConvertToGB($sizeBytes) {
    return [math]::Round($sizeBytes / 1GB, 2)
}

# Connect to vCenter
try {
    $connection = Connect-VIServer -Server $Server -User $Username -Password $Password -ErrorAction Stop
    
    if (-not $AsJson) {
        Write-Host "Connected to $Server" -ForegroundColor Green
    }
} 
catch {
    Write-Host "Error connecting to vSphere: $_" -ForegroundColor Red
    exit 1
}

try {
    # Structure to hold infrastructure info for JSON output
    $infraInfo = [PSCustomObject]@{
        datacenter = ""
        clusters = @()
    }
    
    # Get datacenter
    if ($Datacenter) {
        $dc = Get-Datacenter -Name $Datacenter -ErrorAction SilentlyContinue
    } else {
        $dc = Get-Datacenter -ErrorAction SilentlyContinue | Select-Object -First 1
    }
    
    if (-not $dc) {
        $availableDCs = Get-Datacenter
        if ($availableDCs) {
            Write-Host "Available datacenters:" -ForegroundColor Yellow
            $availableDCs | ForEach-Object { Write-Host "- $($_.Name)" }
            Write-Host "`nPlease specify a datacenter using the -Datacenter parameter."
        } else {
            Write-Host "No datacenters found. Please check your vSphere environment."
        }
        exit 1
    }
    
    if (-not $AsJson) {
        Write-Host "Using datacenter: $($dc.Name)" -ForegroundColor Cyan
    }
    
    $infraInfo.datacenter = $dc.Name
    
    # Get all clusters in the datacenter
    $clusters = Get-Cluster -Location $dc
    
    if (-not $clusters) {
        Write-Host "No clusters found in the selected datacenter."
        exit 0
    }
    
    # Process each cluster
    foreach ($cluster in $clusters) {
        $clusterInfo = [PSCustomObject]@{
            name = $cluster.Name
            datastore_clusters = @()
            standalone_datastores = @()
        }
        
        if (-not $AsJson) {
            Write-Host "`nCluster: $($cluster.Name)" -ForegroundColor Cyan
            Write-Host ("-" * ($cluster.Name.Length + 9))
        }
        
        # Get datastores accessible by this cluster
        $clusterDatastores = Get-Datastore -RelatedObject $cluster
        
        # Get datastore clusters (storage pods)
        $storagePods = Get-DatastoreCluster -Location $dc
        
        # Process storage pods
        if (-not $storagePods) {
            if (-not $AsJson) {
                Write-Host "  No datastore clusters found for this cluster"
            }
        } else {
            foreach ($pod in $storagePods) {
                $dsClusterInfo = [PSCustomObject]@{
                    name = $pod.Name
                    datastores = @()
                }
                
                if (-not $AsJson) {
                    Write-Host "  Datastore Cluster: $($pod.Name)"
                }
                
                # Get datastores in this pod that are accessible by this cluster
                $podDatastores = $pod | Get-Datastore | Where-Object { $clusterDatastores -contains $_ }
                
                if (-not $podDatastores) {
                    if (-not $AsJson) {
                        Write-Host "    No datastores from this cluster in this datastore cluster"
                    }
                } else {
                    foreach ($ds in $podDatastores) {
                        $capacityGB = ConvertToGB $ds.CapacityGB
                        $freeSpaceGB = ConvertToGB $ds.FreeSpaceGB
                        
                        if ($AsJson) {
                            $dsInfo = [PSCustomObject]@{
                                name = $ds.Name
                                capacity_gb = $capacityGB
                                free_space_gb = $freeSpaceGB
                            }
                            $dsClusterInfo.datastores += $dsInfo
                        } else {
                            Write-Host "    - $($ds.Name) (Capacity: $capacityGB GB, Free: $freeSpaceGB GB)"
                        }
                    }
                }
                
                # Add datastore cluster to cluster info if it has datastores
                if ($AsJson -and $dsClusterInfo.datastores.Count -gt 0) {
                    $clusterInfo.datastore_clusters += $dsClusterInfo
                }
            }
        }
        
        # Get all datastore clusters' datastores to find standalone ones
        $allPodDatastores = @()
        if ($storagePods) {
            $allPodDatastores = $storagePods | Get-Datastore
        }
        
        # Process standalone datastores
        if (-not $AsJson) {
            Write-Host "  Standalone Datastores:"
        }
        
        $standaloneDatastores = $clusterDatastores | Where-Object { $allPodDatastores -notcontains $_ }
        
        if (-not $standaloneDatastores) {
            if (-not $AsJson) {
                Write-Host "    No standalone datastores found"
            }
        } else {
            foreach ($ds in $standaloneDatastores) {
                $capacityGB = ConvertToGB $ds.CapacityGB
                $freeSpaceGB = ConvertToGB $ds.FreeSpaceGB
                
                if ($AsJson) {
                    $dsInfo = [PSCustomObject]@{
                        name = $ds.Name
                        capacity_gb = $capacityGB
                        free_space_gb = $freeSpaceGB
                    }
                    $clusterInfo.standalone_datastores += $dsInfo
                } else {
                    Write-Host "    - $($ds.Name) (Capacity: $capacityGB GB, Free: $freeSpaceGB GB)"
                }
            }
        }
        
        # Add cluster info to infrastructure info
        if ($AsJson) {
            $infraInfo.clusters += $clusterInfo
        }
    }
    
    # Output JSON if requested
    if ($AsJson) {
        $jsonOutput = $infraInfo | ConvertTo-Json -Depth 10
        Write-Host $jsonOutput
    }
}
catch {
    Write-Host "Error: $_" -ForegroundColor Red
}
finally {
    # Disconnect from vCenter
    Disconnect-VIServer -Server $connection -Confirm:$false -ErrorAction SilentlyContinue
} 