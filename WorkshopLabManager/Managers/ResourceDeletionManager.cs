using Azure;
using Azure.ResourceManager.Compute;
using Azure.ResourceManager.EventHubs;
using Azure.ResourceManager.Sql;
using Azure.ResourceManager.Storage;
using WorkshopLabManager.Helpers;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;


namespace WorkshopLabManager.Managers
{
	public static class ResourceDeletionManager
	{
		public const int MaxDop = 20;  // Too large a number can result in diminishing returns, Azure rate limiting, and/or throttling with 429 (too many requests) errors

		public static async Task DeleteResources(string attendeeName = null)
		{
			var attendee = attendeeName == null ? null : new AttendeeInfo(attendeeName);

			var virtualMachineCount = await GetVirtualMachineCount(attendee);
			var sqlDatabaseServers = await GetSqlDatabaseServers(attendee);
			var eventHubNamespaces = await GetEventHubNamespaces(attendee);
			var storageAccounts = await GetStorageAccounts(attendee);

			var totalDeletes = virtualMachineCount + sqlDatabaseServers.Length + eventHubNamespaces.Length + storageAccounts.Length;
			if (totalDeletes == 0)
			{
				ConsoleHelper.WriteLine("Nothing to delete.");
				return;
			}

			ConsoleHelper.WriteLine();
			ConsoleHelper.WriteLine($"Resources to delete ({totalDeletes}):");
			ConsoleHelper.WriteLine($" - Virtual machines:      {virtualMachineCount,3}");
			ConsoleHelper.WriteLine($" - SQL database servers:  {sqlDatabaseServers.Length,3}");
			ConsoleHelper.WriteLine($" - Event hub namespaces:  {eventHubNamespaces.Length,3}");
			ConsoleHelper.WriteLine($" - Storage accounts:      {storageAccounts.Length,3}");
			ConsoleHelper.WriteLine();

			if (!ConsoleHelper.ConfirmYesNo($"Are you sure you want to delete {totalDeletes} resource(s)"))
			{
				return;
			}

			var operationCounter = 0;
			var sqlDatabaseServersDeletedCount = 0;
			var eventHubNamespacesDeletedCount = 0;
			var storageAccountsDeletedCount = 0;

			var options = new ParallelOptions { MaxDegreeOfParallelism = MaxDop };
			var started = DateTime.Now;

			var virtualMachineTask = default(Task);
			if (attendee != null)
			{
				var vmName = VirtualMachineHelper.GetVmName(attendee);
				if (!VirtualMachineHelper.IsAttendeeVm(vmName))
				{
					virtualMachineTask = Task.CompletedTask;
				}
				else
				{
					virtualMachineTask = VirtualMachineHelper.DeleteVirtualMachine(vmName, Interlocked.Increment(ref operationCounter), CancellationToken.None);
				}
			}
			else
			{
				var attendeeVmNames = new List<string>();
				await foreach (var vm in Program.Context.TargetResourceGroup.GetVirtualMachines().GetAllAsync())
				{
					if (VirtualMachineHelper.IsAttendeeVm(vm.Data.Name))
					{
						attendeeVmNames.Add(vm.Data.Name);
					}
				}

				var pmOptions = new ParallelOptions { MaxDegreeOfParallelism = MaxDop };
				virtualMachineTask = Parallel.ForEachAsync(attendeeVmNames, pmOptions, async (vmName, _ct) =>
				{
					var current = Interlocked.Increment(ref operationCounter);
					try
					{
						await VirtualMachineHelper.DeleteVirtualMachine(vmName, current, CancellationToken.None);
					}
					catch (Exception ex)
					{
						ConsoleHelper.WriteLine($"{current,3}: Error deleting VM {vmName}: {ex.Message}", ConsoleColor.Red);
					}
				});
			}

			var sqlDatabaseTask = Parallel.ForEachAsync(sqlDatabaseServers, options, async (sqlDatabaseServer, cancellationToken) =>
			{
				var current = Interlocked.Increment(ref operationCounter);
				var name = sqlDatabaseServer.Data.Name;

				try
				{
					ConsoleHelper.WriteLine($"{current,3}: Deleting SQL database server: {name}", ConsoleColor.Green);
					await sqlDatabaseServer.DeleteAsync(WaitUntil.Completed, cancellationToken);
					Interlocked.Increment(ref sqlDatabaseServersDeletedCount);
				}
				catch (Exception ex)
				{
					ConsoleHelper.WriteLine($"{current,3}: Error deleting SQL database server {name}: {ex.Message}", ConsoleColor.Red);
				}
			});

			var eventHubTask = Parallel.ForEachAsync(eventHubNamespaces, options, async (eventHubNamespace, cancellationToken) =>
			{
				var current = Interlocked.Increment(ref operationCounter);
				var name = eventHubNamespace.Data.Name;

				try
				{
					ConsoleHelper.WriteLine($"{current,3}: Deleting event hub namespace: {name}", ConsoleColor.Green);
					await eventHubNamespace.DeleteAsync(WaitUntil.Completed, cancellationToken);
					Interlocked.Increment(ref eventHubNamespacesDeletedCount);
				}
				catch (Exception ex)
				{
					ConsoleHelper.WriteLine($"{current,3}: Error deleting event hub namespace {name}: {ex.Message}", ConsoleColor.Red);
				}
			});

			var storageAccountTask = Parallel.ForEachAsync(storageAccounts, options, async (storageAccount, cancellationToken) =>
			{
				var current = Interlocked.Increment(ref operationCounter);
				var name = storageAccount.Data.Name;

				try
				{
					ConsoleHelper.WriteLine($"{current,3}: Deleting storage account: {name}", ConsoleColor.Green);
					await storageAccount.DeleteAsync(WaitUntil.Completed, cancellationToken);
					Interlocked.Increment(ref storageAccountsDeletedCount);
				}
				catch (Exception ex)
				{
					ConsoleHelper.WriteLine($"{current,3}: Error deleting storage account {name}: {ex.Message}", ConsoleColor.Red);
				}
			});

			await Task.WhenAll(sqlDatabaseTask, eventHubTask, storageAccountTask, virtualMachineTask);

			Console.ResetColor();

			var elapsed = DateTime.Now.Subtract(started);
			ConsoleHelper.WriteLine();
			ConsoleHelper.WriteLine($"Deleted {totalDeletes} resource(s) in {elapsed}");
		}

		private static async Task<int> GetVirtualMachineCount(AttendeeInfo attendee)
		{
			var vmCount = 0;
			await foreach (var vm in Program.Context.TargetResourceGroup.GetVirtualMachines().GetAllAsync())
			{
				var vmName = vm.Data.Name;
				if (VirtualMachineHelper.IsAttendeeVm(vmName) && (attendee == null || vmName == VirtualMachineHelper.GetVmName(attendee)))
				{
					vmCount++;
				}
			}

			return vmCount;
		}

		private static async Task<SqlServerResource[]> GetSqlDatabaseServers(AttendeeInfo attendee)
		{
			var list = new List<SqlServerResource>();

			await foreach (var resource in Program.Context.TargetResourceGroup.GetSqlServers().GetAllAsync())
			{
				if (attendee == null)
				{
					list.Add(resource);
				}
				else
				{
					var targetName = $"{Program.Context.AppConfig.SqlDatabase.ServerNamePrefix}-{attendee.AttendeeNameIdentifier}";
					if (resource.Data.Name == targetName)
					{
						list.Add(resource);
					}
				}
			}

			return list.ToArray();
		}

		private static async Task<EventHubsNamespaceResource[]> GetEventHubNamespaces(AttendeeInfo attendee)
		{
			var list = new List<EventHubsNamespaceResource>();

			await foreach (var resource in Program.Context.TargetResourceGroup.GetEventHubsNamespaces().GetAllAsync())
			{
				if (attendee == null)
				{
					list.Add(resource);
				}
				else
				{
					var targetName = $"{Program.Context.AppConfig.EventHub.NamespaceNamePrefix}-{attendee.AttendeeNameIdentifier}";
					if (resource.Data.Name == targetName)
					{
						list.Add(resource);
					}
				}
			}

			return list.ToArray();
		}

		private static async Task<StorageAccountResource[]> GetStorageAccounts(AttendeeInfo attendee)
		{
			var list = new List<StorageAccountResource>();

			await foreach (var resource in Program.Context.TargetResourceGroup.GetStorageAccounts().GetAllAsync())
			{
				if (attendee == null)
				{
					list.Add(resource);
				}
				else
				{
					var targetName = StorageAccountHelper.BuildStorageAccountName(attendee.AttendeeNameIdentifier);
					if (resource.Data.Name == targetName)
					{
						list.Add(resource);
					}
				}
			}

			return list.ToArray();
		}

	}
}
