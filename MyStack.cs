using Pulumi;
using Pulumi.Azure.Core;
using Pulumi.Azure.Storage;
using Pulumi.Azure.Network;
using Pulumi.Azure.Network.Inputs;
using Pulumi.Azure.ContainerService;
using Pulumi.Azure.ContainerService.Inputs;
using System;
using System.Collections.Generic;

class MyStack : Stack
{
    private readonly string _uniqueid = DateTime.Now.ToString("yyyyMMdd");
    private const string _azureGatewayNetworkUniqueName = "default";

    public MyStack()
    {
        // Create an Azure Resource Group
        var applicationResourceGroup = new ResourceGroup("my-application", new ResourceGroupArgs()
        { 
            Name = "my-application"
        });

        var networkResourceGroup = new ResourceGroup("my-network", new ResourceGroupArgs()
        {
            Name = "my-network"
        });

        // Create an Azure Storage Account
        var storageAccount = new Account("storage", new AccountArgs
        {
            ResourceGroupName = applicationResourceGroup.Name,
            AccountReplicationType = "LRS",
            AccountTier = "Standard"
        });

        var vnet = new VirtualNetwork("myvnet", new VirtualNetworkArgs()
        {
            AddressSpaces = "10.0.0.0/16",
            Subnets = new List<VirtualNetworkSubnetsArgs>()
            {
                //Do not use inline subnets here, its broken: https://www.terraform.io/docs/providers/azurerm/r/subnet.html
            },
            ResourceGroupName = networkResourceGroup.Name,
            Name = $"myvnet{_uniqueid}"
        });

        var appGatewaySubnet = new Subnet("gateway-subnet", new SubnetArgs()
        {
            Name = _azureGatewayNetworkUniqueName,
            VirtualNetworkName = $"myvnet{_uniqueid}",
            AddressPrefix = "10.0.1.0/24",
            ResourceGroupName = networkResourceGroup.Name,
        }, new CustomResourceOptions() {  DependsOn = { vnet } });

        var applicationSubnet = new Subnet("application-subnet", new SubnetArgs()
        {
            Name = "applicationsubnet",
            VirtualNetworkName = $"myvnet{_uniqueid}",
            AddressPrefix = "10.0.0.0/24",
            ResourceGroupName = networkResourceGroup.Name,
            Delegations = new SubnetDelegationsArgs()
            { 
                Name = "SubnetDelegation",
                ServiceDelegation = new SubnetDelegationsServiceDelegationArgs()
                {
                    Name = "Microsoft.ContainerInstance/containerGroups",
                    Actions = "Microsoft.Network/virtualNetworks/subnets/join/action"
                }
            }
        }, new CustomResourceOptions() { DependsOn = { vnet } });

        var applicationNetworkProfile = new Profile("application-networking-profile", new ProfileArgs()
        {
            Name = "application-networking-profile",
            ResourceGroupName = networkResourceGroup.Name,
            ContainerNetworkInterface = new ProfileContainerNetworkInterfaceArgs()
            {
                Name = "ContainerNetworkInterface",
                IpConfigurations = new ProfileContainerNetworkInterfaceIpConfigurationsArgs()
                {
                    Name = "IpConfigurations",
                    SubnetId = applicationSubnet.Id,
                }
            }
        });

        var aci = new Group($"application{_uniqueid}", new GroupArgs()
        {
            Containers = new GroupContainersArgs()
            {
                Name = $"application{_uniqueid}",
                Memory = 4,
                Cpu = 2,
                Image = "nginx",
                Ports = new GroupContainersPortsArgs()
                { 
                    Port = 9000,
                    Protocol = "TCP"
                },
            },
            OsType = "Linux",
            ResourceGroupName = applicationResourceGroup.Name,
            Name = $"application{_uniqueid}",
            IpAddressType = "Private",
            NetworkProfileId = applicationNetworkProfile.Id,
        });

        

        var publicIp = new PublicIp("gatewaypublicip", new PublicIpArgs()
        {
            Sku = "Standard",
            ResourceGroupName = networkResourceGroup.Name,
            AllocationMethod = "Static",
            DomainNameLabel = $"application{_uniqueid}"
        });
        var backendAddressPools = new ApplicationGatewayBackendAddressPoolsArgs()
        {
            Name = "applicationBackendPool",
            Fqdns = new InputList<string> 
            {
                aci.Fqdn
            },
        };
        var frontendPorts = new ApplicationGatewayFrontendPortsArgs()
        {
            Name = "sqfrontendport",
            Port = 80
        };
        var frontendIpConfigurations = new ApplicationGatewayFrontendIpConfigurationsArgs()
        {
            Name = "appGwFrontEndIpConfig",
            PublicIpAddressId = publicIp.Id,
        };
        var backendHttpSettings = new ApplicationGatewayBackendHttpSettingsArgs()
        {
            Name = "sqBackendHttpSettings",
            Port = 9000,
            Protocol = "Http",
            CookieBasedAffinity = "Disabled",
            RequestTimeout = 30,            
        };
        var appgwHttpListener = new ApplicationGatewayHttpListenersArgs()
        {
            Name = "appgwhttplistener",
            FrontendPortId = frontendPorts.Id,
            FrontendPortName = frontendPorts.Name,
            HostName = publicIp.Fqdn,
            Protocol = "Http",
            FrontendIpConfigurationId = frontendIpConfigurations.Id,
            FrontendIpConfigurationName = frontendIpConfigurations.Name,
        };
        var requestRoutingRules = new ApplicationGatewayRequestRoutingRulesArgs()
        {
            Name = "RoutingRequestRules",
            BackendAddressPoolId = backendAddressPools.Id,
            BackendAddressPoolName = backendAddressPools.Name,
            BackendHttpSettingsId = backendHttpSettings.Id,
            BackendHttpSettingsName = backendHttpSettings.Name,
            HttpListenerId = appgwHttpListener.Id,
            HttpListenerName = appgwHttpListener.Name,
            RuleType = "Basic",            
        };
        
        new ApplicationGateway($"myappgw{_uniqueid}", new ApplicationGatewayArgs()
        {
            ResourceGroupName = networkResourceGroup.Name,
            Sku = new ApplicationGatewaySkuArgs()
            {
                Name = "Standard_v2",
                Tier = "Standard_v2",
                Capacity = 1
            },
            FrontendIpConfigurations = frontendIpConfigurations,
            BackendAddressPools = backendAddressPools,
            BackendHttpSettings = backendHttpSettings,
            FrontendPorts = frontendPorts,
            RequestRoutingRules = requestRoutingRules,
            GatewayIpConfigurations = new ApplicationGatewayGatewayIpConfigurationsArgs()
            {
                Name = "gatewayconfig",
                SubnetId = appGatewaySubnet.Id
            },
            HttpListeners = appgwHttpListener,
        });

        this.ApplicationUrl = publicIp.Fqdn;
    }

    [Output]
    public Output<string> ApplicationUrl { get; set; }

}
