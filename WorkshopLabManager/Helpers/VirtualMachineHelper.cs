using Azure;
using Azure.Core;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using Azure.ResourceManager.Network;
using Azure.ResourceManager.Network.Models;
using Azure.ResourceManager.Resources;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WorkshopLabManager.Helpers
{
	public static class VirtualMachineHelper
	{
		public static string GetVmName(AttendeeInfo attendee) =>
			$"{Program.Context.AppConfig.VirtualMachine.Clone.VmNamePrefix}{attendee.AttendeeNameIdentifier}";

		public static bool IsAttendeeVm(string name) =>
			name.StartsWith(Program.Context.AppConfig.VirtualMachine.Clone.VmNamePrefix);

		public static async Task<bool> CreateVirtualMachineResourcesAsync(
			AttendeeInfo attendee,
			string regionName,
			int counter,
			CancellationToken cancellationToken)
		{
			var resourceGroup = Program.Context.TargetResourceGroup;

			var vmName = GetVmName(attendee);
			var vmSize = Program.Context.AppConfig.VirtualMachine.Clone.VmSize;
			var pipName = $"{vmName}-pip";
			var nicName = $"{vmName}-nic";
			var nsgName = $"{vmName}-nsg";

			var existingVm = default(VirtualMachineResource);
			try
			{
				existingVm = (await resourceGroup.GetVirtualMachines().GetAsync(vmName, expand: null, cancellationToken)).Value;
			}
			catch
			{
			}

			if (existingVm != null)
			{
				ConsoleHelper.WriteLine($"{counter,3}: Skipping VM: {vmName} (already exists)", ConsoleColor.DarkGreen);
				return false;
			}

			var vnetName = $"{vmName}-vnet";
			var subnetName = "default";

			var subnet = await CreateVnetAndSubnet(resourceGroup, counter, regionName, vnetName, subnetName, vmName);           
			
			var pip = await CreatePublicIpAddress(resourceGroup, counter, pipName, regionName);

			var nsg = await CreateNsgWithRdp(resourceGroup, counter, nsgName, regionName);

			var nic = await CreateNic(resourceGroup, counter, nicName, subnet, pip, nsg, regionName);

			var galleryImageVersionId = $"/subscriptions/{Program.Context.SubscriptionData.SubscriptionId}/resourceGroups/{Program.Context.AppConfig.SourceResourceGroupName}/providers/Microsoft.Compute/galleries/{Program.Context.AppConfig.VirtualMachine.Publish.GalleryName}/images/{Program.Context.AppConfig.VirtualMachine.Publish.ImageName}/versions/{Program.Context.AppConfig.VirtualMachine.Publish.ImageVersion}";
			
			await CreateVm(resourceGroup, vmName, vmSize, nic, galleryImageVersionId, regionName, cancellationToken);

			return true;
		}

		private static async Task<SubnetResource> CreateVnetAndSubnet(
			ResourceGroupResource targetResourceGroup,
			int counter,
			string regionName,
			string vnetName,
			string subnetName,
			string vmName)
		{
			var location = new AzureLocation(regionName);
			var vnetCollection = targetResourceGroup.GetVirtualNetworks();

			var (vnetPrefix, subnetPrefix) = ComputePerVmCidrs(vmName);

			var vnetData = new VirtualNetworkData
			{
				Location = location
			};

			vnetData.AddressPrefixes.Add(vnetPrefix);

			vnetData.Subnets.Add(new SubnetData
			{
				Name = subnetName,
				AddressPrefix = subnetPrefix
			});

			var operation = await vnetCollection.CreateOrUpdateAsync(WaitUntil.Completed, vnetName, vnetData);
			var vnet = operation.Value;

			ConsoleHelper.WriteLine($"   {counter,3}.1 Created VNet: {vnetName} ({vnetPrefix}), subnet {subnetName} ({subnetPrefix}) in {regionName}", ConsoleColor.DarkGreen);

			var subnet = (await vnet.GetSubnets().GetAsync(subnetName)).Value;

			return subnet;
		}

		private static (string vnetPrefix, string subnetPrefix) ComputePerVmCidrs(string vmName)
		{
			// Give each VM a unique, deterministic /16 VNet and a /24 default subnet inside it.
			// Example: VNet 10.<A>.0.0/16 and Subnet 10.<A>.<B>.0/24

			using var sha = SHA256.Create();
			var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(vmName));

			// Avoid 0 and 255; pick somewhat “middle” ranges to reduce accidental conflicts.
			var a = 10 + (bytes[0] % 200);  // 10..209
			var b = 10 + (bytes[1] % 200);  // 10..209

			var vnetPrefix = $"10.{a}.0.0/16";
			var subnetPrefix = $"10.{a}.{b}.0/24";

			return (vnetPrefix, subnetPrefix);
		}

		private static async Task<PublicIPAddressResource> CreatePublicIpAddress(
			ResourceGroupResource resourceGroup,
			int counter,
			string pipName,
			string regionName)
		{
			var location = new AzureLocation(regionName);

			var pips = resourceGroup.GetPublicIPAddresses();

			var createData = new PublicIPAddressData
			{
				Location = location,
				Sku = new PublicIPAddressSku { Name = PublicIPAddressSkuName.Standard },
				PublicIPAllocationMethod = NetworkIPAllocationMethod.Static
			};

			var createOperation = await pips.CreateOrUpdateAsync(WaitUntil.Completed, pipName, createData);
			ConsoleHelper.WriteLine($"   {counter,3}.2 Created Public IP: {pipName} (Standard/Static)", ConsoleColor.DarkGreen);

			return createOperation.Value;
		}

		private static async Task<NetworkSecurityGroupResource> CreateNsgWithRdp(
			ResourceGroupResource resourceGroup,
			int counter,
			string nsgName,
			string regionName)
		{
			var location = new AzureLocation(regionName);

			var nsgs = resourceGroup.GetNetworkSecurityGroups();
			var create = new NetworkSecurityGroupData { Location = location };
			var nsg = (await nsgs.CreateOrUpdateAsync(WaitUntil.Completed, nsgName, create)).Value;

			const string ruleName = "Allow-RDP";
			var rule = new SecurityRuleData
			{
				Name = ruleName,
				Priority = 1000,
				Direction = SecurityRuleDirection.Inbound,
				Access = SecurityRuleAccess.Allow,
				Protocol = SecurityRuleProtocol.Tcp,
				SourcePortRange = "*",
				DestinationPortRange = "3389",
				SourceAddressPrefix = "*",
				DestinationAddressPrefix = "*"
			};
			var rules = nsg.GetSecurityRules();
			await rules.CreateOrUpdateAsync(WaitUntil.Completed, ruleName, rule);
			ConsoleHelper.WriteLine($"   {counter,3}.3 Created NSG: {nsgName} in {location}", ConsoleColor.DarkGreen);

			return nsg;
		}

		private static async Task<NetworkInterfaceResource> CreateNic(
			ResourceGroupResource resourceGroup,
			int counter,
			string nicName,
			SubnetResource subnet,
			PublicIPAddressResource pip,
			NetworkSecurityGroupResource nsg,
			string regionName)
		{
			var location = new AzureLocation(regionName);

			var nics = resourceGroup.GetNetworkInterfaces();

			var data = new NetworkInterfaceData
			{
				Location = location,
				NetworkSecurityGroup = new NetworkSecurityGroupData { Id = nsg.Id }
			};

			data.IPConfigurations.Add(new NetworkInterfaceIPConfigurationData
			{
				Name = "ipconfig1",
				Primary = true,
				Subnet = new SubnetData { Id = subnet.Data.Id },
				PublicIPAddress = new PublicIPAddressData { Id = pip.Id }
			});

			var operation = await nics.CreateOrUpdateAsync(WaitUntil.Completed, nicName, data);
			ConsoleHelper.WriteLine($"   {counter,3}.4 Created NIC: {nicName} in {location}", ConsoleColor.DarkGreen);
			
			return operation.Value;
		}

		private static async Task CreateVm(
			ResourceGroupResource rg,
			string vmName,
			string vmSize,
			NetworkInterfaceResource nic,
			string galleryImageVersionId,
			string regionName,
			CancellationToken cancellationToken)
		{
			var location = new AzureLocation(regionName);

			var vms = rg.GetVirtualMachines();

			var vm = new VirtualMachineData(location)
			{
				HardwareProfile = new VirtualMachineHardwareProfile
				{
					VmSize = new VirtualMachineSizeType(vmSize)
				},
				NetworkProfile = new VirtualMachineNetworkProfile(),
				StorageProfile = new VirtualMachineStorageProfile()
			};

			vm.NetworkProfile.NetworkInterfaces.Add(new VirtualMachineNetworkInterfaceReference
			{
				Id = nic.Id,
				Primary = true
			});

			vm.StorageProfile.ImageReference = new ImageReference
			{
				Id = new ResourceIdentifier(galleryImageVersionId)
			};

			vm.StorageProfile.OSDisk = new VirtualMachineOSDisk(DiskCreateOptionType.FromImage)
			{
				Name = $"{vmName}-osdisk",
				DeleteOption = DiskDeleteOptionType.Delete
			};

			vm.SecurityProfile = new SecurityProfile
			{
				SecurityType = SecurityType.TrustedLaunch
			};

			await vms.CreateOrUpdateAsync(WaitUntil.Completed, vmName, vm, cancellationToken);
		}

		public static async Task<string> GetIpAddressAsync(AttendeeInfo attendee, CancellationToken cancellationToken)
		{
			var pipName = $"{Program.Context.AppConfig.VirtualMachine.Clone.VmNamePrefix}{attendee.AttendeeNameIdentifier}-pip";
			var ipAddress = default(string);

			try
			{
				var pipCollection = Program.Context.TargetResourceGroup.GetPublicIPAddresses();

				var response = await pipCollection.GetIfExistsAsync(pipName, expand: null, cancellationToken);

				if (response.HasValue)
				{
					ipAddress = response.Value.Data.IPAddress;
				}
			}
			catch
			{
				ipAddress = "0.0.0.0";
			}

			return ipAddress;
		}


		public static async Task DeleteVirtualMachine(string vmName, int counter, CancellationToken ct)
		{
			if (!IsAttendeeVm(vmName))
			{
				ConsoleHelper.WriteLine($"{counter,3}: [SKIP] Non-attendee VM: {vmName}", ConsoleColor.DarkYellow);
				return;
			}

			var resourceGroup = Program.Context.TargetResourceGroup;

			// -------- Resolve VM (no CT overloads on GetAsync(name) in your SDK) --------
			var vm = default(VirtualMachineResource);
			try
			{
				vm = (await resourceGroup.GetVirtualMachines().GetAsync(vmName)).Value;
			}
			catch (RequestFailedException ex) when (ex.Status == 404)
			{
				// VM not found
			}
			catch
			{
				// swallow and proceed with best-effort cleanup
			}

			// Collect dependent IDs before VM delete
			var nicId = default(ResourceIdentifier);
			var pipId = default(ResourceIdentifier);
			var nsgId = default(ResourceIdentifier);

			if (vm != null)
			{
				var nicRef = vm.Data.NetworkProfile?.NetworkInterfaces?.FirstOrDefault();
				if (nicRef?.Id != null)
				{
					nicId = new ResourceIdentifier(nicRef.Id);

					try
					{
						var nic = (await resourceGroup.GetNetworkInterfaces().GetAsync(nicId.Name)).Value;
						var ipconfig =
							nic.Data.IPConfigurations.FirstOrDefault(c => c.Primary == true) ??
							nic.Data.IPConfigurations.FirstOrDefault();

						if (ipconfig?.PublicIPAddress?.Id != null)
						{
							pipId = new ResourceIdentifier(ipconfig.PublicIPAddress.Id);
						}

						if (nic.Data.NetworkSecurityGroup?.Id != null)
						{
							nsgId = new ResourceIdentifier(nic.Data.NetworkSecurityGroup.Id);
						}
					}
					catch (Azure.RequestFailedException ex) when (ex.Status == 404)
					{
						// NIC missing; continue
					}
					catch
					{
						// continue best-effort
					}
				}
			}

			// -------- 1) Delete VM --------
			if (vm != null)
			{
				ConsoleHelper.WriteLine($"{counter,3}: Deleting VM: {vmName}", ConsoleColor.Green);
				try
				{
					await vm.DeleteAsync(WaitUntil.Completed, forceDeletion: true, ct);
					ConsoleHelper.WriteLine($"   {counter,3}.1: Deleted VM: {vmName}", ConsoleColor.DarkGreen);
				}
				catch (Exception ex)
				{
					ConsoleHelper.WriteLine($"   {counter,3}.1: Error deleting VM {vmName}: {ex.Message}", ConsoleColor.Red);
				}
			}
			else
			{
				ConsoleHelper.WriteLine($"   {counter,3}.1: No VM found: {vmName}", ConsoleColor.DarkYellow);
			}

			// -------- 2) Delete NIC --------
			if (nicId != null)
			{
				try
				{
					var nics = resourceGroup.GetNetworkInterfaces();
					var nic = (await nics.GetAsync(nicId.Name)).Value; // no CT overload
					await nic.DeleteAsync(WaitUntil.Completed, ct);
					ConsoleHelper.WriteLine($"   {counter,3}.2: Deleted NIC: {nicId.Name}", ConsoleColor.DarkGreen);
				}
				catch (RequestFailedException ex) when (ex.Status == 404)
				{
					// already gone
				}
				catch (Exception ex)
				{
					ConsoleHelper.WriteLine($"   {counter,3}.2: Error deleting NIC {nicId.Name}: {ex.Message}", ConsoleColor.Red);
				}
			}

			// -------- 3) Delete Public IP --------
			if (pipId != null)
			{
				try
				{
					var pips = resourceGroup.GetPublicIPAddresses();
					var pip = (await pips.GetAsync(pipId.Name)).Value; // no CT overload
					await pip.DeleteAsync(WaitUntil.Completed, ct);
					ConsoleHelper.WriteLine($"   {counter,3}.3: Deleted Public IP: {pipId.Name}", ConsoleColor.DarkGreen);
				}
				catch (RequestFailedException ex) when (ex.Status == 404)
				{
					// already gone
				}
				catch (Exception ex)
				{
					ConsoleHelper.WriteLine($"   {counter,3}.3: Error deleting Public IP {pipId.Name}: {ex.Message}", ConsoleColor.Red);
				}
			}

			// -------- 4) Delete NSG (only if it's the per-VM NSG) --------
			if (nsgId != null)
			{
				var expectedNsgName = $"{vmName}-nsg";
				if (string.Equals(nsgId.Name, expectedNsgName, StringComparison.OrdinalIgnoreCase))
				{
					try
					{
						var nsgs = resourceGroup.GetNetworkSecurityGroups();
						var nsg = (await nsgs.GetAsync(nsgId.Name)).Value; // no CT overload
						await nsg.DeleteAsync(WaitUntil.Completed, ct);
						ConsoleHelper.WriteLine($"   {counter,3}.4: Deleted NSG: {nsgId.Name}", ConsoleColor.DarkGreen);
					}
					catch (RequestFailedException ex) when (ex.Status == 404)
					{
						// already gone
					}
					catch (Exception ex)
					{
						ConsoleHelper.WriteLine($"   {counter,3}.4: Error deleting NSG {nsgId.Name}: {ex.Message}", ConsoleColor.Red);
					}
				}
				else
				{
					ConsoleHelper.WriteLine($"   {counter,3}.4: [SKIP] NSG not owned by this VM pattern: {nsgId.Name}", ConsoleColor.DarkYellow);
				}
			}

			// 5) Delete per-VM VNet (best-effort)
			try
			{
				var vnetName = $"{vmName}-vnet";
				var vnet = (await resourceGroup.GetVirtualNetworks().GetAsync(vnetName)).Value;

				await vnet.DeleteAsync(WaitUntil.Completed, ct);
				ConsoleHelper.WriteLine($"   {counter,3}.5: Deleted VNet: {vnetName}", ConsoleColor.DarkGreen);
			}
			catch (RequestFailedException ex) when (ex.Status == 404)
			{
				ConsoleHelper.WriteLine($"   {counter,3}.5: [SKIP] VNet not found for {vmName}", ConsoleColor.DarkYellow);
			}
			catch (Exception ex)
			{
				ConsoleHelper.WriteLine($"   {counter,3}.5: Error deleting VNet for {vmName}: {ex.Message}", ConsoleColor.Red);
			}
		}

	}
}
