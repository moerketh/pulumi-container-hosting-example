using Pulumi;
using Pulumi.Azure.Compute;
using Pulumi.Azure.Core;
using Pulumi.Azure.Network;
using Pulumi.Azure.Network.Inputs;
using Pulumi.Azure.ContainerService;
using Pulumi.Azure.ContainerService.Inputs;
using Pulumi.Docker;
using System;
using System.Collections.Generic;

class MyStack : Stack
{
    private const string AzureGatewayNetworkName = "default";

    public MyStack()
    {
        // Create an Azure Resource Group
        var applicationResourceGroup = new ResourceGroup("pulumi-appgateway-hosting-example", new ResourceGroupArgs()
        { 
            Name = "pulumi-appgateway-hosting-example"
        });
        var registry = new Registry("global", new RegistryArgs
        {
            ResourceGroupName = applicationResourceGroup.Name,
            AdminEnabled = true,
            Sku = "Premium",
        });
        var dockerImage = new Pulumi.Docker.Image("my-app", new Pulumi.Docker.ImageArgs
        {
            ImageName = Output.Format($"{registry.LoginServer}/myapp:v1.0.0"),
            Build = "./container",
            Registry = new ImageRegistry
            {
                Server = registry.LoginServer,
                Username = registry.AdminUsername,
                Password = registry.AdminPassword,
            },
        }, new ComponentResourceOptions { Parent = registry });
        var vnet = new VirtualNetwork("vnet", new VirtualNetworkArgs()
        {
            AddressSpaces = "10.0.0.0/16",
            Subnets = new List<VirtualNetworkSubnetsArgs>()
            {},
            ResourceGroupName = applicationResourceGroup.Name,
            Name = $"vnet"
        });

        var appGatewaySubnet = new Subnet("gateway-subnet", new SubnetArgs()
        {
            Name = AzureGatewayNetworkName,
            VirtualNetworkName = $"vnet",
            AddressPrefix = "10.0.1.0/24",
            ResourceGroupName = applicationResourceGroup.Name,
        }, new CustomResourceOptions() { Parent = vnet });

        var applicationSubnet = new Subnet("application-subnet", new SubnetArgs()
        {
            Name = "applicationsubnet",
            VirtualNetworkName = $"vnet",
            AddressPrefix = "10.0.0.0/24",
            ResourceGroupName = applicationResourceGroup.Name,
            Delegations = new SubnetDelegationsArgs()
            {
                Name = "SubnetDelegation",
                ServiceDelegation = new SubnetDelegationsServiceDelegationArgs()
                {
                    Name = "Microsoft.ContainerInstance/containerGroups",
                    Actions = "Microsoft.Network/virtualNetworks/subnets/join/action"
                }
            }
        }, new CustomResourceOptions() { Parent = vnet });
        var applicationNetworkProfile = new Profile("application-networking-profile", new ProfileArgs()
        {
            Name = "application-networking-profile",
            ResourceGroupName = applicationResourceGroup.Name,
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
        var aci = new Group($"aci", new GroupArgs()
        {
            Containers = new GroupContainersArgs()
            {
                Name = "aci",
                Memory = 1.5,
                Cpu = 0.5,
                Image = dockerImage.ImageName,
                Ports = new GroupContainersPortsArgs()
                { 
                    Port = 80,
                    Protocol = "TCP"
                },
            },
            ImageRegistryCredentials = new GroupImageRegistryCredentialsArgs
            {
                Server = registry.LoginServer,
                Username = registry.AdminUsername,
                Password = registry.AdminPassword,
            },
            OsType = "Linux",
            ResourceGroupName = applicationResourceGroup.Name,
            Name = "application",
            IpAddressType = "Private",
            NetworkProfileId = applicationNetworkProfile.Id,
        });
        var publicIp = new PublicIp("gateway-publicip", new PublicIpArgs()
        {
            Sku = "Standard",
            ResourceGroupName = applicationResourceGroup.Name,
            AllocationMethod = "Static",
            DomainNameLabel = "application" + Guid.NewGuid().ToString()
        });
        var backendAddressPools = new ApplicationGatewayBackendAddressPoolsArgs()
        {
            Name = "applicationBackendPool",
            IpAddresses = aci.IpAddress
        };
        var frontendPorts = new ApplicationGatewayFrontendPortsArgs()
        {
            Name = "applicationfrontendport",
            Port = 80
        };
        var frontendIpConfigurations = new ApplicationGatewayFrontendIpConfigurationsArgs()
        {
            Name = "applicationFrontEndIpConfig",
            PublicIpAddressId = publicIp.Id,
        };
        var backendHttpSettings = new ApplicationGatewayBackendHttpSettingsArgs()
        {
            Name = "sqBackendHttpSettings",
            Port = 80,
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
        new ApplicationGateway($"applicationgateway", new ApplicationGatewayArgs()
        {
            ResourceGroupName = applicationResourceGroup.Name,
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
