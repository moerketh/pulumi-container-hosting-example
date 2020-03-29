# Pulumi Application Gateway container hosting example
This example builds and deploys a container image in Azure Container Instances
Virtual Network integration is used to host the container in a virtual network (instead of public url), this feature is currently preview in some regions. Please check the [Azure Container Instances documentation](https://docs.microsoft.com/en-us/azure/container-instances/container-instances-vnet) for the current rollout status. An Application Gateway is used as the web front end. 
Note: SSL should be added to the Application Gateway but is not part of this example.

## How to use this example
Clone this repository
[Configure the azure connection](https://www.pulumi.com/docs/intro/cloud-providers/azure/setup/), for exaple with: `az login`
Set the Azure Region you want to deploy to: `pulumi config set azure:location WestEurope`
Run `pulumi up` to try this code!

For details on how to use Pulumi, please review their [Azure Quickstart](https://www.pulumi.com/docs/get-started/azure/)

## Some learnings creating this example
I found the Windows application to be very much a work-in-progress at this time. In particular I've found the following bugs to hinder my progress:
[The app does not terminate on error](https://github.com/pulumi/pulumi/issues/3275)
Often times a generic "transport is closing" error is thrown instead of a helpful error. See [here](https://github.com/pulumi/pulumi-policy/issues/24), [here](https://github.com/pulumi/pulumi/issues/2713) for examples.
However, the Linux version works excellent with WSL2 on Windows 10.