using Azure.Core;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.EventHubs;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Sql;
using Azure.ResourceManager.Storage;
using WorkshopLabManager.Helpers;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WorkshopLabManager.Managers
{
	public static class ResourceListManager
	{
		public static async Task ListResources()
		{
			ConsoleHelper.WriteLine();
			ConsoleHelper.WriteLine($"Attendee resources in resource group '{Program.Context.AppConfig.TargetResourceGroupName}':", ConsoleColor.White);

			var counter = 0;

			// SQL servers
			await foreach (var sqlServer in Program.Context.TargetResourceGroup.GetSqlServers().GetAllAsync())
			{
				ConsoleHelper.WriteLine($"{++counter,3}: SQL database server: {sqlServer.Data.Name}", ConsoleColor.Green);
			}

			// Event Hub namespaces
			await foreach (var eventHubsNamespace in Program.Context.TargetResourceGroup.GetEventHubsNamespaces().GetAllAsync())
			{
				ConsoleHelper.WriteLine($"{++counter,3}: Event hub namespace: {eventHubsNamespace.Data.Name}", ConsoleColor.Green);
			}

			// Storage accounts
			await foreach (var storage in Program.Context.TargetResourceGroup.GetStorageAccounts().GetAllAsync())
			{
				ConsoleHelper.WriteLine($"{++counter,3}: Storage account: {storage.Data.Name}", ConsoleColor.Green);
			}

			// Virtual machines (pure SDK)
			await foreach (var vm in Program.Context.TargetResourceGroup.GetVirtualMachines().GetAllAsync())
			{
				if (!VirtualMachineHelper.IsAttendeeVm(vm.Data.Name))
				{
					continue;
				}

				var publicIp = default(string);
				try
				{
					publicIp = await TryGetVmPublicIpAsync(vm, CancellationToken.None) ?? "(no IP)";
				}
				catch (Exception ex)
				{
					publicIp = $"(error: {ex.Message})";
				}

				ConsoleHelper.WriteLine($"{++counter,3}: Virtual machine: {vm.Data.Name,-40}  Public IP: {publicIp}", ConsoleColor.Green);
			}

			ConsoleHelper.WriteLine();
			ConsoleHelper.WriteLine($"Total attendee resources: {counter}");
		}

		// Walk VM -> primary NIC -> primary IP config -> Public IP -> IPAddress
		private static async Task<string> TryGetVmPublicIpAsync(VirtualMachineResource vm, CancellationToken ct)
		{
			// Get first NIC reference from VM (primary)
			var nicRef = vm.Data.NetworkProfile?.NetworkInterfaces?.FirstOrDefault();
			if (nicRef == null || nicRef.Id == null)
			{
				return null;
			}

			// Resolve NIC from its resource ID
			var nicId = new ResourceIdentifier(nicRef.Id);
			var nic = await Program.Context.TargetResourceGroup.GetNetworkInterfaces().GetAsync(nicId.Name);
			var ipconfig = nic.Value.Data.IPConfigurations.FirstOrDefault(c => c.Primary == true) ?? nic.Value.Data.IPConfigurations.FirstOrDefault();
			if (ipconfig?.PublicIPAddress == null || ipconfig.PublicIPAddress.Id == null)
			{
				return null;
			}

			// Resolve Public IP from its resource ID and return the assigned address
			var pipId = new ResourceIdentifier(ipconfig.PublicIPAddress.Id);
			var pip = await Program.Context.TargetResourceGroup.GetPublicIPAddresses().GetAsync(pipId.Name);
			return pip.Value.Data.IPAddress; // may be null if not yet allocated
		}
	}
}
