package main

import (
	"context"
	"encoding/json"
	"flag"
	"fmt"
	"net/url"
	"os"
	"strings"

	"github.com/vmware/govmomi"
	"github.com/vmware/govmomi/find"
	"github.com/vmware/govmomi/object"
	"github.com/vmware/govmomi/property"
	"github.com/vmware/govmomi/session"
	"github.com/vmware/govmomi/vim25"
	"github.com/vmware/govmomi/vim25/mo"
	"github.com/vmware/govmomi/vim25/soap"
	"github.com/vmware/govmomi/vim25/types"
)

// Config holds our connection parameters
type Config struct {
	URL        string
	Username   string
	Password   string
	Insecure   bool
	Datacenter string
	OutputJSON bool
}

// DatastoreInfo represents information about a datastore
type DatastoreInfo struct {
	Name      string  `json:"name"`
	Capacity  float64 `json:"capacity_gb"`
	FreeSpace float64 `json:"free_space_gb"`
}

// DatastoreClusterInfo represents information about a datastore cluster
type DatastoreClusterInfo struct {
	Name       string          `json:"name"`
	Datastores []DatastoreInfo `json:"datastores"`
}

// ClusterInfo represents information about a cluster
type ClusterInfo struct {
	Name                 string                 `json:"name"`
	DatastoreClusters    []DatastoreClusterInfo `json:"datastore_clusters"`
	StandaloneDatastores []DatastoreInfo        `json:"standalone_datastores"`
}

// InfrastructureInfo represents the entire infrastructure
type InfrastructureInfo struct {
	Datacenter string        `json:"datacenter"`
	Clusters   []ClusterInfo `json:"clusters"`
}

func main() {
	ctx := context.Background()

	// Parse command line flags
	cfg := parseFlags()

	// Connect to vSphere
	client, err := connectToVSphere(ctx, cfg)
	if err != nil {
		fmt.Printf("Error connecting to vSphere: %s\n", err)
		os.Exit(1)
	}
	defer client.Logout(ctx)

	// Create the finder
	finder := find.NewFinder(client.Client, true)

	// Set datacenter for the finder
	var dc *object.Datacenter
	if cfg.Datacenter != "" {
		dc, err = finder.Datacenter(ctx, cfg.Datacenter)
	} else {
		// Try to find the default datacenter
		dc, err = finder.DefaultDatacenter(ctx)
	}

	if err != nil {
		// If we can't find a specific datacenter, list all datacenters and exit
		dcs, err := finder.DatacenterList(ctx, "*")
		if err != nil {
			fmt.Printf("Error: %s\n", err)
			os.Exit(1)
		}

		if len(dcs) == 0 {
			fmt.Println("No datacenters found. Please check your vSphere environment.")
			os.Exit(1)
		}

		fmt.Println("Available datacenters:")
		for _, dc := range dcs {
			fmt.Printf("- %s\n", dc.Name())
		}
		fmt.Println("\nPlease specify a datacenter using the -datacenter flag.")
		os.Exit(1)
	}

	// Set the datacenter on the finder
	finder.SetDatacenter(dc)

	if !cfg.OutputJSON {
		fmt.Printf("Using datacenter: %s\n", dc.Name())
	}

	// Get all clusters
	clusters, err := finder.ClusterComputeResourceList(ctx, "*")
	if err != nil {
		fmt.Printf("Error getting clusters: %s\n", err)
		os.Exit(1)
	}

	if len(clusters) == 0 {
		fmt.Println("No clusters found in the selected datacenter.")
		os.Exit(0)
	}

	// Initialize the infrastructure info object if using JSON output
	var infraInfo InfrastructureInfo
	if cfg.OutputJSON {
		infraInfo.Datacenter = dc.Name()
		infraInfo.Clusters = make([]ClusterInfo, 0, len(clusters))
	}

	// For each cluster, get datastore clusters and datastores
	for _, cluster := range clusters {
		var clusterInfo ClusterInfo
		if cfg.OutputJSON {
			clusterInfo.Name = cluster.Name()
			clusterInfo.DatastoreClusters = make([]DatastoreClusterInfo, 0)
			clusterInfo.StandaloneDatastores = make([]DatastoreInfo, 0)
		} else {
			fmt.Printf("\nCluster: %s\n", cluster.Name())
			fmt.Println(strings.Repeat("-", len(cluster.Name())+9))
		}

		// Get datastore clusters (StoragePods)
		pc := property.DefaultCollector(client.Client)
		var storagePods []mo.StoragePod

		// Find storage pods using inventory path lookup
		podRefs := []types.ManagedObjectReference{}

		// Use FolderList to find datastore folders
		datastoreFolders, err := finder.FolderList(ctx, "*/datastores")
		if err != nil {
			// Try another approach - might be a single folder
			datastoreFolders, err = finder.FolderList(ctx, "*/datastore")
			if err != nil {
				// Try direct path
				datastoreFolders, err = finder.FolderList(ctx, fmt.Sprintf("%s/datastore", dc.InventoryPath))
				if err != nil {
					if !cfg.OutputJSON {
						fmt.Printf("  Error finding datastore folders: %s\n", err)
					}
					continue
				}
			}
		}

		// For each datastore folder, check its children for StoragePods
		for _, dsFolder := range datastoreFolders {
			children, err := dsFolder.Children(ctx)
			if err != nil {
				continue
			}

			for _, child := range children {
				if pod, ok := child.(*object.StoragePod); ok {
					var podInfo mo.StoragePod
					err = pc.RetrieveOne(ctx, pod.Reference(), []string{"name", "childEntity"}, &podInfo)
					if err != nil {
						continue
					}
					storagePods = append(storagePods, podInfo)
					podRefs = append(podRefs, pod.Reference())
				}
			}
		}

		// Get datastores accessible by this cluster
		var clusterMo mo.ClusterComputeResource
		err = pc.RetrieveOne(ctx, cluster.Reference(), []string{"datastore"}, &clusterMo)
		if err != nil {
			if !cfg.OutputJSON {
				fmt.Printf("  Error getting cluster details: %s\n", err)
			}
			continue
		}

		// Create a map of all datastores to look up later
		datastoreMap := make(map[string]mo.Datastore)
		var dsList []types.ManagedObjectReference
		for _, ds := range clusterMo.Datastore {
			dsList = append(dsList, ds)
		}

		var datastores []mo.Datastore
		err = pc.Retrieve(ctx, dsList, []string{"name", "summary"}, &datastores)
		if err != nil {
			if !cfg.OutputJSON {
				fmt.Printf("  Error retrieving datastore details: %s\n", err)
			}
			continue
		}

		for _, ds := range datastores {
			datastoreMap[ds.Reference().Value] = ds
		}

		// Display datastore clusters and their datastores
		if len(storagePods) == 0 {
			if !cfg.OutputJSON {
				fmt.Println("  No datastore clusters found for this cluster")
			}
		} else {
			for _, pod := range storagePods {
				var dsClusterInfo DatastoreClusterInfo
				if cfg.OutputJSON {
					dsClusterInfo.Name = pod.Name
					dsClusterInfo.Datastores = make([]DatastoreInfo, 0)
				} else {
					fmt.Printf("  Datastore Cluster: %s\n", pod.Name)
				}

				// Check if this datastore cluster has datastores in this cluster
				podHasDatastoresInCluster := false

				for _, childRef := range pod.ChildEntity {
					if ds, exists := datastoreMap[childRef.Value]; exists {
						if !podHasDatastoresInCluster {
							podHasDatastoresInCluster = true
						}
						capacity := float64(ds.Summary.Capacity) / (1024 * 1024 * 1024)
						freeSpace := float64(ds.Summary.FreeSpace) / (1024 * 1024 * 1024)

						if cfg.OutputJSON {
							dsClusterInfo.Datastores = append(dsClusterInfo.Datastores, DatastoreInfo{
								Name:      ds.Name,
								Capacity:  capacity,
								FreeSpace: freeSpace,
							})
						} else {
							fmt.Printf("    - %s (Capacity: %.2f GB, Free: %.2f GB)\n",
								ds.Name, capacity, freeSpace)
						}
					}
				}

				if !podHasDatastoresInCluster {
					if !cfg.OutputJSON {
						fmt.Println("    No datastores from this cluster in this datastore cluster")
					}
				}

				if cfg.OutputJSON && len(dsClusterInfo.Datastores) > 0 {
					clusterInfo.DatastoreClusters = append(clusterInfo.DatastoreClusters, dsClusterInfo)
				}
			}
		}

		// Display standalone datastores (not in any datastore cluster)
		if !cfg.OutputJSON {
			fmt.Println("  Standalone Datastores:")
		}
		standaloneDsFound := false

		for _, ds := range datastores {
			// Check if this datastore belongs to any storage pod
			belongsToStoragePod := false
			for _, pod := range storagePods {
				for _, childRef := range pod.ChildEntity {
					if childRef.Value == ds.Reference().Value {
						belongsToStoragePod = true
						break
					}
				}
				if belongsToStoragePod {
					break
				}
			}

			if !belongsToStoragePod {
				standaloneDsFound = true
				capacity := float64(ds.Summary.Capacity) / (1024 * 1024 * 1024)
				freeSpace := float64(ds.Summary.FreeSpace) / (1024 * 1024 * 1024)

				if cfg.OutputJSON {
					clusterInfo.StandaloneDatastores = append(clusterInfo.StandaloneDatastores, DatastoreInfo{
						Name:      ds.Name,
						Capacity:  capacity,
						FreeSpace: freeSpace,
					})
				} else {
					fmt.Printf("    - %s (Capacity: %.2f GB, Free: %.2f GB)\n",
						ds.Name, capacity, freeSpace)
				}
			}
		}

		if !standaloneDsFound && !cfg.OutputJSON {
			fmt.Println("    No standalone datastores found")
		}

		if cfg.OutputJSON {
			infraInfo.Clusters = append(infraInfo.Clusters, clusterInfo)
		}
	}

	// Output JSON if requested
	if cfg.OutputJSON {
		jsonOutput, err := json.MarshalIndent(infraInfo, "", "  ")
		if err != nil {
			fmt.Printf("Error generating JSON output: %s\n", err)
			os.Exit(1)
		}
		fmt.Println(string(jsonOutput))
	}
}

// parseFlags parses command line flags
func parseFlags() *Config {
	cfg := &Config{}

	flag.StringVar(&cfg.URL, "url", os.Getenv("VSPHERE_URL"), "vSphere URL (can also set VSPHERE_URL env var)")
	flag.StringVar(&cfg.Username, "username", os.Getenv("VSPHERE_USERNAME"), "vSphere username (can also set VSPHERE_USERNAME env var)")
	flag.StringVar(&cfg.Password, "password", os.Getenv("VSPHERE_PASSWORD"), "vSphere password (can also set VSPHERE_PASSWORD env var)")
	flag.BoolVar(&cfg.Insecure, "insecure", true, "Skip verification of server certificate")
	flag.StringVar(&cfg.Datacenter, "datacenter", os.Getenv("VSPHERE_DATACENTER"), "vSphere datacenter name (can also set VSPHERE_DATACENTER env var)")
	flag.BoolVar(&cfg.OutputJSON, "o", false, "Output format (use 'json' for JSON output)")

	flag.Parse()

	if cfg.URL == "" || cfg.Username == "" || cfg.Password == "" {
		fmt.Println("Must specify vSphere URL, username, and password")
		fmt.Println("Usage:")
		flag.PrintDefaults()
		os.Exit(1)
	}

	return cfg
}

// connectToVSphere establishes a connection to the vSphere server
func connectToVSphere(ctx context.Context, cfg *Config) (*govmomi.Client, error) {
	u, err := soap.ParseURL(cfg.URL)
	if err != nil {
		return nil, err
	}

	u.User = url.UserPassword(cfg.Username, cfg.Password)

	// Set up the client
	soapClient := soap.NewClient(u, cfg.Insecure)
	vimClient, err := vim25.NewClient(ctx, soapClient)
	if err != nil {
		return nil, err
	}

	// Create the session manager
	sm := session.NewManager(vimClient)

	// Create a govmomi client
	client := &govmomi.Client{
		Client:         vimClient,
		SessionManager: sm,
	}

	// Login
	err = sm.Login(ctx, u.User)
	if err != nil {
		return nil, err
	}

	return client, nil
}
