/*
 * IMPORTANT NOTE ABOUT THIS IMPLEMENTATION:
 * 
 * This file provides a skeleton implementation of a vSphere client using the VMware.Vim library.
 * However, VMware.Vim is not available as a standard NuGet package.
 * 
 * To use this code, you'll need to:
 * 1. Download the VMware vSphere Management SDK from VMware Developer Center
 *    (https://developer.vmware.com/web/sdk/8.0/vsphere-management)
 * 2. Add references to the VMware.Vim.dll and related assemblies
 * 
 * See the README.md file for more detailed instructions.
 * 
 * Alternative options:
 * - Use PowerCLI (PowerShell module for vSphere)
 * - Use the VMware.vSphere.Automation.SDK
 * - Implement a REST client using the vSphere REST API
 */

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using VMware.Vim;

namespace GodcinfoClient
{
    // Models for JSON output
    public class DatastoreInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("capacity_gb")]
        public double CapacityGB { get; set; }

        [JsonProperty("free_space_gb")]
        public double FreeSpaceGB { get; set; }
    }

    public class DatastoreClusterInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("datastores")]
        public List<DatastoreInfo> Datastores { get; set; } = new List<DatastoreInfo>();
    }

    public class ClusterInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; } = string.Empty;

        [JsonProperty("datastore_clusters")]
        public List<DatastoreClusterInfo> DatastoreClusters { get; set; } = new List<DatastoreClusterInfo>();

        [JsonProperty("standalone_datastores")]
        public List<DatastoreInfo> StandaloneDatastores { get; set; } = new List<DatastoreInfo>();
    }

    public class InfrastructureInfo
    {
        [JsonProperty("datacenter")]
        public string Datacenter { get; set; } = string.Empty;

        [JsonProperty("clusters")]
        public List<ClusterInfo> Clusters { get; set; } = new List<ClusterInfo>();
    }

    class Program
    {
        static async Task<int> Main(string[] args)
        {
            // Define command line options
            var rootCommand = new RootCommand("VMware vSphere Datastore Information Client");

            var urlOption = new Option<string>(
                aliases: new[] { "--url", "-u" },
                description: "vSphere URL (can also be set via VSPHERE_URL environment variable)");
            urlOption.SetDefaultValueFactory(() => Environment.GetEnvironmentVariable("VSPHERE_URL") ?? string.Empty);
            rootCommand.AddOption(urlOption);

            var usernameOption = new Option<string>(
                aliases: new[] { "--username", "-n" },
                description: "vSphere username (can also be set via VSPHERE_USERNAME environment variable)");
            usernameOption.SetDefaultValueFactory(() => Environment.GetEnvironmentVariable("VSPHERE_USERNAME") ?? string.Empty);
            rootCommand.AddOption(usernameOption);

            var passwordOption = new Option<string>(
                aliases: new[] { "--password", "-p" },
                description: "vSphere password (can also be set via VSPHERE_PASSWORD environment variable)");
            passwordOption.SetDefaultValueFactory(() => Environment.GetEnvironmentVariable("VSPHERE_PASSWORD") ?? string.Empty);
            rootCommand.AddOption(passwordOption);

            var insecureOption = new Option<bool>(
                aliases: new[] { "--insecure", "-k" },
                description: "Skip verification of server certificate",
                getDefaultValue: () => true);
            rootCommand.AddOption(insecureOption);

            var datacenterOption = new Option<string>(
                aliases: new[] { "--datacenter", "-d" },
                description: "vSphere datacenter name (can also be set via VSPHERE_DATACENTER environment variable)");
            datacenterOption.SetDefaultValueFactory(() => Environment.GetEnvironmentVariable("VSPHERE_DATACENTER") ?? string.Empty);
            rootCommand.AddOption(datacenterOption);

            var outputOption = new Option<string>(
                aliases: new[] { "--output", "-o" },
                description: "Output format (json or text)",
                getDefaultValue: () => "text");
            rootCommand.AddOption(outputOption);

            rootCommand.SetHandler(async (string url, string username, string password, bool insecure, string datacenter, string output) =>
            {
                // Validate parameters
                if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                {
                    Console.WriteLine("Error: Must specify vSphere URL, username, and password");
                    return 1;
                }

                try
                {
                    await RunVSphereQueryAsync(url, username, password, insecure, datacenter, output == "json");
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error: {ex.Message}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"Inner Error: {ex.InnerException.Message}");
                    }
                    return 1;
                }
            }, urlOption, usernameOption, passwordOption, insecureOption, datacenterOption, outputOption);

            return await rootCommand.InvokeAsync(args);
        }

        static async Task RunVSphereQueryAsync(string url, string username, string password, bool insecure, string datacenterName, bool outputJson)
        {
            // Create connection
            var vimClient = await ConnectToVSphereAsync(url, username, password, insecure);
            if (vimClient == null)
            {
                throw new Exception("Failed to initialize vSphere client");
            }

            try
            {
                var infraInfo = new InfrastructureInfo();
                
                // Find datacenter
                ManagedObjectReference? dcMoref = null;
                var dcRef = await FindDatacenterAsync(vimClient, datacenterName);
                if (dcRef == null)
                {
                    // List available datacenters and exit
                    await ListAvailableDatacentersAsync(vimClient);
                    return;
                }
                
                dcMoref = dcRef;
                string dcName = await GetManagedObjectNameAsync(vimClient, dcMoref);
                
                if (!outputJson)
                {
                    Console.WriteLine($"Using datacenter: {dcName}");
                }
                
                infraInfo.Datacenter = dcName;

                // Get all clusters in the datacenter
                var clusterRefs = await FindClustersInDatacenterAsync(vimClient, dcMoref);
                if (clusterRefs.Count == 0)
                {
                    Console.WriteLine("No clusters found in the selected datacenter.");
                    return;
                }

                // Process each cluster
                foreach (var clusterRef in clusterRefs)
                {
                    string clusterName = await GetManagedObjectNameAsync(vimClient, clusterRef);
                    var clusterInfo = new ClusterInfo { Name = clusterName };

                    if (!outputJson)
                    {
                        Console.WriteLine($"\nCluster: {clusterName}");
                        Console.WriteLine(new string('-', clusterName.Length + 9));
                    }

                    // Get datastore folders in the datacenter
                    var datastoreFolderRefs = await FindDatastoreFoldersAsync(vimClient, dcMoref);
                    
                    // Find storage pods (datastore clusters)
                    var storagePods = new List<ManagedObjectReference>();
                    foreach (var folderRef in datastoreFolderRefs)
                    {
                        var podRefs = await FindStoragePodsInFolderAsync(vimClient, folderRef);
                        storagePods.AddRange(podRefs);
                    }

                    // Get datastores accessible by this cluster
                    var clusterDatastores = await GetClusterDatastoresAsync(vimClient, clusterRef);
                    
                    // Create a map of all datastores info
                    var datastoreInfoMap = await GetDatastoresInfoAsync(vimClient, clusterDatastores);

                    // Process storage pods (datastore clusters)
                    if (storagePods.Count == 0)
                    {
                        if (!outputJson)
                        {
                            Console.WriteLine("  No datastore clusters found for this cluster");
                        }
                    }
                    else
                    {
                        foreach (var podRef in storagePods)
                        {
                            string podName = await GetManagedObjectNameAsync(vimClient, podRef);
                            var podChildren = await GetStoragePodChildrenAsync(vimClient, podRef);
                            
                            var dsClusterInfo = new DatastoreClusterInfo { Name = podName };
                            
                            if (!outputJson)
                            {
                                Console.WriteLine($"  Datastore Cluster: {podName}");
                            }

                            bool hasDatastoresInCluster = false;
                            
                            // Check if this datastore cluster has datastores in this cluster
                            foreach (var childRef in podChildren)
                            {
                                if (datastoreInfoMap.ContainsKey(childRef.Value))
                                {
                                    hasDatastoresInCluster = true;
                                    var dsInfo = datastoreInfoMap[childRef.Value];
                                    
                                    if (outputJson)
                                    {
                                        dsClusterInfo.Datastores.Add(new DatastoreInfo
                                        {
                                            Name = dsInfo.name,
                                            CapacityGB = dsInfo.capacityGB,
                                            FreeSpaceGB = dsInfo.freeSpaceGB
                                        });
                                    }
                                    else
                                    {
                                        Console.WriteLine($"    - {dsInfo.name} (Capacity: {dsInfo.capacityGB:F2} GB, Free: {dsInfo.freeSpaceGB:F2} GB)");
                                    }
                                }
                            }

                            if (!hasDatastoresInCluster && !outputJson)
                            {
                                Console.WriteLine("    No datastores from this cluster in this datastore cluster");
                            }

                            // Add datastore cluster to cluster info if it has datastores
                            if (outputJson && dsClusterInfo.Datastores.Count > 0)
                            {
                                clusterInfo.DatastoreClusters.Add(dsClusterInfo);
                            }
                        }
                    }

                    // Find standalone datastores (not in any datastore cluster)
                    if (!outputJson)
                    {
                        Console.WriteLine("  Standalone Datastores:");
                    }
                    
                    bool standaloneDsFound = false;
                    
                    // Keep track of datastores belonging to storage pods
                    var podDatastores = new HashSet<string>();
                    foreach (var podRef in storagePods)
                    {
                        var podChildren = await GetStoragePodChildrenAsync(vimClient, podRef);
                        foreach (var childRef in podChildren)
                        {
                            podDatastores.Add(childRef.Value);
                        }
                    }
                    
                    // Check each datastore to see if it's standalone
                    foreach (var dsRef in clusterDatastores)
                    {
                        if (!podDatastores.Contains(dsRef.Value) && datastoreInfoMap.ContainsKey(dsRef.Value))
                        {
                            standaloneDsFound = true;
                            var dsInfo = datastoreInfoMap[dsRef.Value];
                            
                            if (outputJson)
                            {
                                clusterInfo.StandaloneDatastores.Add(new DatastoreInfo
                                {
                                    Name = dsInfo.name,
                                    CapacityGB = dsInfo.capacityGB,
                                    FreeSpaceGB = dsInfo.freeSpaceGB
                                });
                            }
                            else
                            {
                                Console.WriteLine($"    - {dsInfo.name} (Capacity: {dsInfo.capacityGB:F2} GB, Free: {dsInfo.freeSpaceGB:F2} GB)");
                            }
                        }
                    }
                    
                    if (!standaloneDsFound && !outputJson)
                    {
                        Console.WriteLine("    No standalone datastores found");
                    }
                    
                    // Add cluster info to infrastructure info
                    if (outputJson)
                    {
                        infraInfo.Clusters.Add(clusterInfo);
                    }
                }
                
                // Output JSON if requested
                if (outputJson)
                {
                    string jsonOutput = JsonConvert.SerializeObject(infraInfo, Formatting.Indented);
                    Console.WriteLine(jsonOutput);
                }
            }
            finally
            {
                await vimClient.Logout();
            }
        }

        static async Task<VimClient> ConnectToVSphereAsync(string url, string username, string password, bool insecure)
        {
            var vim = new VimClient();
            
            // Skip SSL verification if insecure is true
            if (insecure)
            {
                ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true;
            }
            
            try
            {
                await vim.ConnectAsync(new Uri(url), username, password);
                return vim;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to connect to vSphere: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                throw;
            }
        }

        static async Task<ManagedObjectReference?> FindDatacenterAsync(VimClient vim, string datacenterName)
        {
            // Try to find the specific datacenter if name provided
            if (!string.IsNullOrEmpty(datacenterName))
            {
                try
                {
                    var dcRef = await vim.FindEntityViewAsync<Datacenter>(vim.ServiceContent.RootFolder, datacenterName);
                    if (dcRef != null)
                    {
                        return dcRef.MoRef;
                    }
                }
                catch
                {
                    // If not found, we'll try to get the default or list all datacenters
                }
            }

            // Try to find any datacenter if name not provided
            try
            {
                var dcs = await vim.FindEntityViewsAsync<Datacenter>(vim.ServiceContent.RootFolder, null, null, null);
                if (dcs.Count > 0)
                {
                    return dcs[0].MoRef;
                }
            }
            catch
            {
                // If no datacenters found, we'll return null
            }

            return null;
        }

        static async Task ListAvailableDatacentersAsync(VimClient vim)
        {
            var dcs = await vim.FindEntityViewsAsync<Datacenter>(vim.ServiceContent.RootFolder, null, null, null);
            if (dcs.Count == 0)
            {
                Console.WriteLine("No datacenters found. Please check your vSphere environment.");
                return;
            }

            Console.WriteLine("Available datacenters:");
            foreach (var dc in dcs)
            {
                Console.WriteLine($"- {dc.Name}");
            }
            Console.WriteLine("\nPlease specify a datacenter using the -datacenter flag.");
        }

        static async Task<string> GetManagedObjectNameAsync(VimClient vim, ManagedObjectReference moRef)
        {
            // Get the name property for a managed object
            var entityView = await vim.GetViewAsync<VirtualMachineRelocateSpec>(moRef, new[] { "name" });
            return entityView.Name;
        }

        static async Task<List<ManagedObjectReference>> FindClustersInDatacenterAsync(VimClient vim, ManagedObjectReference dcMoref)
        {
            // Find clusters in the datacenter
            var dcView = await vim.GetViewAsync<Datacenter>(dcMoref, new[] { "hostFolder" });
            var clusters = await vim.FindEntityViewsAsync<ClusterComputeResource>(dcView.HostFolder, null, null, null);
            
            var result = new List<ManagedObjectReference>();
            foreach (var cluster in clusters)
            {
                result.Add(cluster.MoRef);
            }
            
            return result;
        }

        static async Task<List<ManagedObjectReference>> FindDatastoreFoldersAsync(VimClient vim, ManagedObjectReference dcMoref)
        {
            // Get the datastore folder from the datacenter
            var dcView = await vim.GetViewAsync<Datacenter>(dcMoref, new[] { "datastoreFolder" });
            var result = new List<ManagedObjectReference> { dcView.DatastoreFolder };
            return result;
        }

        static async Task<List<ManagedObjectReference>> FindStoragePodsInFolderAsync(VimClient vim, ManagedObjectReference folderRef)
        {
            // Find storage pods (datastore clusters) in the folder
            var storagePods = await vim.FindEntityViewsAsync<StoragePod>(folderRef, null, null, null);
            
            var result = new List<ManagedObjectReference>();
            foreach (var pod in storagePods)
            {
                result.Add(pod.MoRef);
            }
            
            return result;
        }

        static async Task<List<ManagedObjectReference>> GetClusterDatastoresAsync(VimClient vim, ManagedObjectReference clusterRef)
        {
            // Get datastores accessible by a cluster
            var clusterView = await vim.GetViewAsync<ClusterComputeResource>(clusterRef, new[] { "datastore" });
            return clusterView.Datastore;
        }

        static async Task<List<ManagedObjectReference>> GetStoragePodChildrenAsync(VimClient vim, ManagedObjectReference podRef)
        {
            // Get datastores in a storage pod
            var podView = await vim.GetViewAsync<StoragePod>(podRef, new[] { "childEntity" });
            return podView.ChildEntity;
        }

        static async Task<Dictionary<string, (string name, double capacityGB, double freeSpaceGB)>> GetDatastoresInfoAsync(
            VimClient vim, List<ManagedObjectReference> datastoreRefs)
        {
            var result = new Dictionary<string, (string name, double capacityGB, double freeSpaceGB)>();
            
            foreach (var dsRef in datastoreRefs)
            {
                try
                {
                    var dsView = await vim.GetViewAsync<Datastore>(dsRef, new[] { "name", "summary" });
                    double capacityGB = Convert.ToDouble(dsView.Summary.Capacity) / (1024 * 1024 * 1024);
                    double freeSpaceGB = Convert.ToDouble(dsView.Summary.FreeSpace) / (1024 * 1024 * 1024);
                    
                    result[dsRef.Value] = (dsView.Name, capacityGB, freeSpaceGB);
                }
                catch
                {
                    // Skip datastores that can't be accessed
                }
            }
            
            return result;
        }
    }
} 