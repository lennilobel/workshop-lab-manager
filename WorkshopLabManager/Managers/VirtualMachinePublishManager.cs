using Azure;
using Azure.Core;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.Compute.Models;
using WorkshopLabManager.Helpers;
using System;
using System.Threading.Tasks;

namespace WorkshopLabManager.Managers
{
	public static class VirtualMachinePublishManager
	{
		internal static async Task PublishAndReplicate()
		{
			if (!ConsoleHelper.ConfirmYesNo("Are you sure you want to publish and replicate the virtual machine image?"))
			{
				return;
			}

			var sourceResourceGroup = Program.Context.SourceResourceGroup;
			var publishConfig = Program.Context.AppConfig.VirtualMachine.Publish;
			var stepStarted = default(DateTime);
			var started = DateTime.UtcNow;

			// 1) Discover source VM
			Console.WriteLine($"\n[1/10] Discovering source VM '{publishConfig.SourceVmName}'...");
			stepStarted = DateTime.UtcNow;
			var sourceVm = (VirtualMachineResource)await sourceResourceGroup.GetVirtualMachines().GetAsync(publishConfig.SourceVmName);
			var sourceVmLocation = sourceVm.Data.Location;
			Console.WriteLine($"[{DateTime.UtcNow.Subtract(stepStarted)}] Location: {sourceVmLocation}");

			// 2) Deallocate source VM
			Console.WriteLine("\n[2/10] Deallocating source VM...");
			stepStarted = DateTime.UtcNow;
			await sourceVm.DeallocateAsync(WaitUntil.Completed);
			Console.WriteLine($"[{DateTime.UtcNow.Subtract(stepStarted)}] VM deallocated");

			// 3) Get OS disk
			Console.WriteLine("\n[3/10] Resolving source VM OS disk...");
			stepStarted = DateTime.UtcNow;
			var osDiskId = sourceVm.Data.StorageProfile.OSDisk.ManagedDisk.Id;
			Console.WriteLine($"[{DateTime.UtcNow.Subtract(stepStarted)}] OS Disk ID: {osDiskId}");

			// 4) Create snapshot
			Console.WriteLine($"\n[4/10] Creating snapshot '{publishConfig.SnapshotName}'...");
			stepStarted = DateTime.UtcNow;
			var snapshots = sourceResourceGroup.GetSnapshots();
			var snapshotData = new SnapshotData(sourceVmLocation)
			{
				CreationData = new DiskCreationData(DiskCreateOption.Copy)
				{
					SourceResourceId = new ResourceIdentifier(osDiskId)
				}
			};

			var snapshotOp = await snapshots.CreateOrUpdateAsync(WaitUntil.Completed, publishConfig.SnapshotName, snapshotData);
			var snapshot = snapshotOp.Value;
			Console.WriteLine($"[{DateTime.UtcNow.Subtract(stepStarted)}] Snapshot created");

			// 5) Get/create gallery
			Console.WriteLine($"\n[5/10] Get or create gallery '{publishConfig.GalleryName}'...");
			stepStarted = DateTime.UtcNow;
			var galleries = sourceResourceGroup.GetGalleries();
			var gallery = default(GalleryResource);

			if (await galleries.ExistsAsync(publishConfig.GalleryName))
			{
				gallery = await galleries.GetAsync(publishConfig.GalleryName);
				Console.WriteLine($"[{DateTime.UtcNow.Subtract(stepStarted)}] Gallery exists");
			}
			else
			{
				var galleryData = new GalleryData(sourceVmLocation);
				var galleryOp = await galleries.CreateOrUpdateAsync(WaitUntil.Completed, publishConfig.GalleryName, galleryData);
				gallery = galleryOp.Value;
				Console.WriteLine($"[{DateTime.UtcNow.Subtract(stepStarted)}] Gallery created");
			}

			// 6) Get/create image definition
			Console.WriteLine($"\n[6/10] Get or create image definition '{publishConfig.ImageName}'...");
			stepStarted = DateTime.UtcNow;
			var imageDefinitions = gallery.GetGalleryImages();
			var imageDefinition = default(GalleryImageResource);
			if (await imageDefinitions.ExistsAsync(publishConfig.ImageName))
			{
				imageDefinition = await imageDefinitions.GetAsync(publishConfig.ImageName);
				Console.WriteLine($"[{DateTime.UtcNow.Subtract(stepStarted)}] Image definition exists");
			}
			else
			{
				var imageData = new GalleryImageData(sourceVmLocation)
				{
					OSType = SupportedOperatingSystemType.Windows,
					OSState = OperatingSystemStateType.Specialized,
					Identifier = new GalleryImageIdentifier(
						publisher: "WorkshopLabManager",
						offer: "LabTraining",
						sku: "Attendee"),
					HyperVGeneration = HyperVGeneration.V2,
					Features =
					{
						new GalleryImageFeature
						{
							Name = "SecurityType",
							Value = "TrustedLaunch"
						}
					}
				};
				var imageOp = await imageDefinitions.CreateOrUpdateAsync(WaitUntil.Completed, publishConfig.ImageName, imageData);
				imageDefinition = imageOp.Value;
				Console.WriteLine($"[{DateTime.UtcNow.Subtract(stepStarted)}] Image definition created");
			}

			// 7) Delete image version if exists
			Console.WriteLine($"\n[7/10] Delete image version '{publishConfig.ImageVersion}' if it exists...");
			stepStarted = DateTime.UtcNow;
			var versions = imageDefinition.GetGalleryImageVersions();
			if (await versions.ExistsAsync(publishConfig.ImageVersion))
			{
				Console.WriteLine("Existing image version found. Deleting...");
				var existingVersion = (GalleryImageVersionResource)await versions.GetAsync(publishConfig.ImageVersion);
				await existingVersion.DeleteAsync(WaitUntil.Completed);
				Console.WriteLine($"[{DateTime.UtcNow.Subtract(stepStarted)}] Existing image version deleted");
			}

			// 8) Publish image version
			Console.WriteLine($"\n[8/10] Publishing image version '{publishConfig.ImageVersion}'...");
			stepStarted = DateTime.UtcNow;
			var versionData = new GalleryImageVersionData(sourceVmLocation)
			{
				PublishingProfile = new GalleryImageVersionPublishingProfile(),
				StorageProfile = new GalleryImageVersionStorageProfile
				{
					OSDiskImage = new GalleryOSDiskImage
					{
						HostCaching = HostCaching.ReadWrite,

						Source = new GalleryDiskImageSource
						{
							Id = snapshot.Id
						}
					}
				}
			};
			versionData.PublishingProfile.TargetRegions.Add(new TargetRegion(sourceVmLocation.Name));
			var versionOp = await versions.CreateOrUpdateAsync(WaitUntil.Completed, publishConfig.ImageVersion, versionData);
			var version = versionOp.Value;
			Console.WriteLine($"[{DateTime.UtcNow.Subtract(stepStarted)}] Image version published: {version.Id}");

			// 9) Delete snapshot
			Console.WriteLine($"\n[9/10] Deleting snapshot '{publishConfig.SnapshotName}'...");
			stepStarted = DateTime.UtcNow;
			await snapshot.DeleteAsync(WaitUntil.Completed);
			Console.WriteLine($"[{DateTime.UtcNow.Subtract(stepStarted)}] Snapshot deleted");

			// 10) Replicate image
			Console.WriteLine($"\n[10/10] Updating target regions '{string.Join(',', publishConfig.TargetRegionNames)}'...");
			stepStarted = DateTime.UtcNow;
			var patch = new GalleryImageVersionPatch
			{
				PublishingProfile = new GalleryImageVersionPublishingProfile()
			};
			foreach (var region in publishConfig.TargetRegionNames)
			{
				patch.PublishingProfile.TargetRegions.Add(new TargetRegion(region));
			}
			await version.UpdateAsync(WaitUntil.Completed, patch);
			Console.WriteLine($"[{DateTime.UtcNow.Subtract(stepStarted)}] Image publishing and replication completed");

			// Done
			Console.WriteLine($"\nOperation completed");
			Console.WriteLine($"[{DateTime.UtcNow.Subtract(started)}] Done");

		}

	}
}
