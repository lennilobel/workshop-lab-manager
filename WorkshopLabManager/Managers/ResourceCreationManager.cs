using Azure;
using Azure.Core;
using Azure.ResourceManager.EventHubs;
using Azure.ResourceManager.EventHubs.Models;
using Azure.ResourceManager.Sql;
using Azure.ResourceManager.Sql.Models;
using Azure.ResourceManager.Storage;
using Azure.ResourceManager.Storage.Models;
using WorkshopLabManager.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WorkshopLabManager.Managers
{
	public static class ResourceCreationManager
	{
		public const int MaxDop = 10;  // Too large a number can result in diminishing returns, Azure rate limiting, and/or throttling with 429 (too many requests) errors

		public static async Task CreateResources(string attendeeName = null)
		{
			var attendees = attendeeName == null ? Program.Context.Attendees : [new AttendeeInfo(attendeeName)];

			var targetRegionNames = Program.Context.AppConfig.VirtualMachine.Publish.TargetRegionNames;

			var maxVmsPerRegion = Program.Context.AppConfig.VirtualMachine.Clone.MaxVmsPerRegion;

			var attendeeList = attendees.ToList();
			var capacity = targetRegionNames.Length * maxVmsPerRegion;
			if (attendeeList.Count > capacity)
			{
				throw new InvalidOperationException(
					$"Requested {attendeeList.Count} VM(s) but capacity is {capacity} " +
					$"({maxVmsPerRegion} per region × {targetRegionNames.Length}). Add more regions to increase capacity.");
			}

			var attendeeToRegion = new Dictionary<AttendeeInfo, string>(attendeeList.Count);
			for (var index = 0; index < attendeeList.Count; index++)
			{
				attendeeToRegion[attendeeList[index]] = targetRegionNames[Math.Min(index / maxVmsPerRegion, targetRegionNames.Length - 1)];
			}

			if (!ConsoleHelper.ConfirmYesNo($"Are you sure you want to create resources for {attendees.Length} attendee(s)?"))
			{
				return;
			}

			var created = 0;
			var outputLines = new List<string> { "AttendeeName,EmailAddress,SqlDatabaseServerName,EventHubNamespaceName,EventHubSasToken,StorageAccountConnectionString,VirtualMachineIpAddress" };
			var outputLock = new object();
			var options = new ParallelOptions { MaxDegreeOfParallelism = MaxDop };

			var started = DateTime.Now;
			var counter = 0;

			Console.ForegroundColor = ConsoleColor.Green;

			await Parallel.ForEachAsync(attendeeList, options, async (attendee, cancellationToken) =>
			{
				try
				{
					var tasks = new List<Task>();

					if (Program.Context.AppConfig.VirtualMachine.IsEnabled)
					{
						var regionName = attendeeToRegion[attendee];
						tasks.Add(CreateVirtualMachineResourcesAsync(attendee, regionName, Interlocked.Increment(ref counter), cancellationToken));
					}

					if (Program.Context.AppConfig.SqlDatabase.IsEnabled)
					{
						tasks.Add(CreateSqlDatabaseResources(attendee, Interlocked.Increment(ref counter), cancellationToken));
					}

					if (Program.Context.AppConfig.EventHub.IsEnabled)
					{
						tasks.Add(CreateEventHubResources(attendee, Interlocked.Increment(ref counter), cancellationToken));
					}

					if (Program.Context.AppConfig.Storage.IsEnabled)
					{
						tasks.Add(CreateStorageAccountResources(attendee, Interlocked.Increment(ref counter), cancellationToken));
					}

					await Task.WhenAll(tasks);

					var virtualMachineIpAddress = await VirtualMachineHelper.GetIpAddressAsync(attendee, cancellationToken);

					lock (outputLock)
					{
						outputLines.Add($"{attendee.AttendeeName},{attendee.EmailAddress},{attendee.SqlDatabaseServerName},{attendee.EventHubNamespaceName},{attendee.EventHubSasToken},{attendee.StorageAccountConnectionString},{virtualMachineIpAddress}");
					}

					Interlocked.Increment(ref created);
				}
				catch (Exception ex)
				{
					var current = Interlocked.Increment(ref counter);
					ConsoleHelper.WriteLine($"{current,3}: Error creating resources for attendee '{attendee.AttendeeName}': {ex.Message}", ConsoleColor.Red);
				}
			});

			Console.ResetColor();

			var elapsed = DateTime.Now.Subtract(started);

			var sortedLines = outputLines
				.Skip(1)
				.OrderBy(line => line.Split(',')[0])
				.Prepend(outputLines[0])
				.ToArray();

			ConsoleHelper.WriteLine();
			var lineCounter = 0;
			foreach (var line in sortedLines)
			{
				ConsoleHelper.WriteLine($"{++lineCounter,3}: {line}", ConsoleColor.White);
			}
			ConsoleHelper.WriteLine();

			var currentDir = AppContext.BaseDirectory + "\\..\\..\\..";
			var outputPath = Path.Combine(currentDir, "AttendeeResources.csv");
			File.WriteAllLines(outputPath, sortedLines);

			ConsoleHelper.WriteLine($"\nProcessed {attendees.Length} attendee(s); successfully created resources for {created} attendee(s) in {elapsed}");
			ConsoleHelper.WriteLine($"Generated {outputPath}");
		}

		private static async Task CreateVirtualMachineResourcesAsync(AttendeeInfo attendee, string regionName, int counter, CancellationToken cancellationToken)
		{
			ConsoleHelper.WriteLine($"{counter,3}: Creating virtual machine: {Program.Context.AppConfig.VirtualMachine.Clone.VmNamePrefix}{attendee.AttendeeNameIdentifier}", ConsoleColor.Green);
			var started = DateTime.Now;

			var created = await VirtualMachineHelper.CreateVirtualMachineResourcesAsync(attendee, regionName, counter, cancellationToken);

			if (created)
			{
				var elapsed = DateTime.Now.Subtract(started);
				ConsoleHelper.WriteLine($"{counter,3}: Created virtual machine: {Program.Context.AppConfig.VirtualMachine.Clone.VmNamePrefix}{attendee.AttendeeNameIdentifier} in {elapsed}", ConsoleColor.Yellow);
			}
		}

		private static async Task CreateSqlDatabaseResources(AttendeeInfo attendee, int counter, CancellationToken cancellationToken)
		{
			var serverName = $"{Program.Context.AppConfig.SqlDatabase.ServerNamePrefix}-{attendee.AttendeeNameIdentifier}";
			attendee.SqlDatabaseServerName = serverName;

			var sqlServerCollection = Program.Context.TargetResourceGroup.GetSqlServers();

			if (await sqlServerCollection.ExistsAsync(serverName, expand: null, cancellationToken))
			{
				ConsoleHelper.WriteLine($"{counter,3}: Skipping SQL database server: {serverName} (already exists)", ConsoleColor.DarkGreen);
				return;
			}

			var server = await CreateSqlDatabaseServer(counter, serverName, sqlServerCollection, cancellationToken);

			//await CreateEmptyDatabase(Program.Context, attendee, counter, server, databaseName: "HolDb", cancellationToken);
			await CreateAdventureWorksDatabase(attendee, counter, server.Data.Name, cancellationToken);
		}

		private static async Task<SqlServerResource> CreateSqlDatabaseServer(int counter, string serverName, SqlServerCollection sqlServerCollection, CancellationToken cancellationToken)
		{
			ConsoleHelper.WriteLine($"{counter,3}: Creating SQL database server: {serverName}", ConsoleColor.Green);
			var started = DateTime.Now;

			var serverData = new SqlServerData(new AzureLocation(Program.Context.AppConfig.TargetRegionName))
			{
				AdministratorLogin = Program.Context.AppConfig.SqlDatabase.Username,
				AdministratorLoginPassword = Program.Context.AppConfig.SqlDatabase.Password,
				MinTlsVersion = SqlMinimalTlsVersion.Tls1_2,
				PublicNetworkAccess = ServerNetworkAccessFlag.Enabled,
			};

			var serverResource = (await sqlServerCollection.CreateOrUpdateAsync(WaitUntil.Completed, serverName, serverData, cancellationToken)).Value;

			var firewallRules = serverResource.GetSqlFirewallRules();

			await firewallRules.CreateOrUpdateAsync(
				WaitUntil.Completed,
				firewallRuleName: "WideOpen",
				new SqlFirewallRuleData
				{
					StartIPAddress = "0.0.0.0",
					EndIPAddress = "255.255.255.255"
				},
				cancellationToken);

			var elapsed = DateTime.Now.Subtract(started);
			ConsoleHelper.WriteLine($"{counter,3}: Created SQL database server: {serverName} in {elapsed}", ConsoleColor.Yellow);
			return serverResource;
		}

		private static async Task CreateEmptyDatabase(AttendeeInfo attendee, int counter, SqlServerResource server, string databaseName, CancellationToken cancellationToken)
		{
			ConsoleHelper.WriteLine($"     {counter,3}.1: Creating SQL database {databaseName} on server {attendee.SqlDatabaseServerName}", ConsoleColor.Green);
			var started = DateTime.Now;
			await SqlDatabaseHelper.CreateDatabase(server, databaseName, cancellationToken);
			var elapsed = DateTime.Now.Subtract(started);
			ConsoleHelper.WriteLine($"     {counter,3}.1: Created SQL database {databaseName} on server {attendee.SqlDatabaseServerName} in {elapsed}", ConsoleColor.Yellow);
		}

		private static async Task CreateAdventureWorksDatabase(AttendeeInfo attendee, int counter, string serverName, CancellationToken cancellationToken)
		{
			var adventureWorks = Program.Context.AppConfig.SqlDatabase.AdventureWorks;
			ConsoleHelper.WriteLine($"     {counter,3}.1a: Creating SQL database {adventureWorks.DatabaseName} on server {attendee.SqlDatabaseServerName}", ConsoleColor.Green);
			var started = DateTime.Now;

			var createCopySql = $"CREATE DATABASE {adventureWorks.DatabaseName} AS COPY OF {adventureWorks.SourceServerName}.{adventureWorks.DatabaseName}";
			await SqlDatabaseHelper.ExecuteSql(serverName, databaseName: "master", Program.Context.AppConfig.SqlDatabase.Username, Program.Context.AppConfig.SqlDatabase.Password, createCopySql, cancellationToken);

			var sqlServer = await Program.Context.TargetResourceGroup.GetSqlServers().GetAsync(serverName, expand: null, cancellationToken);

			var databases = sqlServer.Value.GetSqlDatabases();

			var database = default(SqlDatabaseResource);
			var deadline = DateTime.UtcNow.AddMinutes(10);
			while (DateTime.UtcNow < deadline)
			{
				if (await databases.ExistsAsync(adventureWorks.DatabaseName, expand: null, filter: null, cancellationToken))
				{
					database = (await databases.GetAsync(adventureWorks.DatabaseName, expand: null, filter: null, cancellationToken)).Value;

					if (database.Data.Status != null && database.Data.Status.Value == SqlDatabaseStatus.Online)
					{
						break;
					}
				}

				await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
			}

			if (database == null)
			{
				throw new InvalidOperationException($"Database '{adventureWorks.DatabaseName}' was not found after creation.");
			}

			ConsoleHelper.WriteLine($"     {counter,3}.1b: Updating database SKU to {Program.Context.AppConfig.SqlDatabase.DatabaseSku} on {attendee.SqlDatabaseServerName}", ConsoleColor.Green);

			var updatedData = database.Data;
			updatedData.Sku = new SqlSku(name: Program.Context.AppConfig.SqlDatabase.DatabaseSku);

			await databases.CreateOrUpdateAsync(
				WaitUntil.Completed,
				adventureWorks.DatabaseName,
				updatedData,
				cancellationToken
			);

			var elapsed = DateTime.Now.Subtract(started);
			ConsoleHelper.WriteLine($"     {counter,3}.1: Created and updated SQL database {adventureWorks.DatabaseName} on server {attendee.SqlDatabaseServerName} in {elapsed}", ConsoleColor.Yellow);
		}

		private static async Task CreateEventHubResources(AttendeeInfo attendeeInfo, int counter, CancellationToken cancellationToken)
		{
			var attendeeNameIdentifier = attendeeInfo.AttendeeNameIdentifier;

			var eventHubNamespaceName = $"{Program.Context.AppConfig.EventHub.NamespaceNamePrefix}-{attendeeNameIdentifier}";
			attendeeInfo.EventHubNamespaceName = eventHubNamespaceName;

			var eventHubNamespaceCollection = Program.Context.TargetResourceGroup.GetEventHubsNamespaces();

			if (await eventHubNamespaceCollection.ExistsAsync(eventHubNamespaceName, cancellationToken))
			{
				ConsoleHelper.WriteLine($"{counter,3}: Skipping event hub namespace: {eventHubNamespaceName} (already exists)", ConsoleColor.DarkGreen);
				attendeeInfo.EventHubSasToken = await GenerateEventHubSasTokenAsync(eventHubNamespaceName);
				return;
			}

			ConsoleHelper.WriteLine($"{counter,3}: Creating event hub namespace: {eventHubNamespaceName}", ConsoleColor.Green);
			var started = DateTime.Now;

			// Create event hubs namespace

			var eventHubNamespaceData = new EventHubsNamespaceData(Program.Context.AppConfig.TargetRegionName)
			{
				Sku = new EventHubsSku(EventHubsSkuName.Basic)
			};

			var eventHubNamespaceResource = await eventHubNamespaceCollection.CreateOrUpdateAsync(WaitUntil.Completed, eventHubNamespaceName, eventHubNamespaceData, cancellationToken);

			// Create event hub

			var eventHubCollection = eventHubNamespaceResource.Value.GetEventHubs();

			var eventHubData = new EventHubData
			{
				RetentionDescription = new RetentionDescription
				{
					CleanupPolicy = CleanupPolicyRetentionDescription.Delete,
					RetentionTimeInHours = 1
				},
			};

			var eventHub = await eventHubCollection.CreateOrUpdateAsync(WaitUntil.Completed, Program.Context.AppConfig.EventHub.EventHubName, eventHubData, cancellationToken);

			// Create event hub authorization rule (policy)

			var eventHubRuleData = new EventHubsAuthorizationRuleData
			{
				Rights =
					{
						EventHubsAccessRight.Manage,
						EventHubsAccessRight.Listen,
						EventHubsAccessRight.Send,
					}
			};

			var eventHubAuthorizationRules = eventHub.Value.GetEventHubAuthorizationRules();

			await eventHubAuthorizationRules.CreateOrUpdateAsync(WaitUntil.Completed, Program.Context.AppConfig.EventHub.PolicyName, eventHubRuleData, cancellationToken);

			// Generate SAS token

			attendeeInfo.EventHubSasToken = await GenerateEventHubSasTokenAsync(eventHubNamespaceName);

			var elapsed = DateTime.Now.Subtract(started);
			ConsoleHelper.WriteLine($"{counter,3}: Created event hub namespace: {eventHubNamespaceName} in {elapsed}", ConsoleColor.Yellow);
		}

		private static async Task<string> GenerateEventHubSasTokenAsync(string namespaceName)
		{
			// Build the full URI to the Event Hub within the namespace
			var resourceUri = $"https://{namespaceName}.servicebus.windows.net/{Program.Context.AppConfig.EventHub.EventHubName}";
			var encodedUri = WebUtility.UrlEncode(resourceUri);

			// Calculate expiry time in seconds since epoch
			var expiry = (int)(DateTime.UtcNow.AddDays(Program.Context.AppConfig.EventHub.SasTokenExpirationDays) - new DateTime(1970, 1, 1)).TotalSeconds;
			var stringToSign = $"{encodedUri}\n{expiry}";

			// Get the EventHub resource
			var namespaceResource = await Program.Context.TargetResourceGroup.GetEventHubsNamespaces().GetAsync(namespaceName);
			var eventHub = await namespaceResource.Value.GetEventHubs().GetAsync(Program.Context.AppConfig.EventHub.EventHubName);
			var policy = await eventHub.Value.GetEventHubAuthorizationRules().GetAsync(Program.Context.AppConfig.EventHub.PolicyName);
			var key = (await policy.Value.GetKeysAsync()).Value.PrimaryKey;

			if (string.IsNullOrEmpty(key))
			{
				throw new InvalidOperationException($"Primary key for policy '{Program.Context.AppConfig.EventHub.PolicyName}' is null or empty.");
			}

			using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
			var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
			var encodedSig = WebUtility.UrlEncode(signature);

			return $"SharedAccessSignature sr={encodedUri}&sig={encodedSig}&se={expiry}&skn={Program.Context.AppConfig.EventHub.PolicyName}";
		}

		private static async Task CreateStorageAccountResources(AttendeeInfo attendeeInfo, int counter, CancellationToken cancellationToken)
		{
			var attendeeNameIdentifier = attendeeInfo.AttendeeNameIdentifier;
			var storageAccountName = StorageAccountHelper.BuildStorageAccountName(attendeeNameIdentifier);
			var storageAccounts = Program.Context.TargetResourceGroup.GetStorageAccounts();
			var storageAccount = default(StorageAccountResource);

			if (await storageAccounts.ExistsAsync(storageAccountName, expand: null, cancellationToken))
			{
				ConsoleHelper.WriteLine($"{counter,3}: Skipping storage account: {storageAccountName} (already exists)", ConsoleColor.DarkGreen);
				storageAccount = (await storageAccounts.GetAsync(storageAccountName, expand: null, cancellationToken)).Value;
			}
			else
			{
				ConsoleHelper.WriteLine($"{counter,3}: Creating storage account: {storageAccountName}", ConsoleColor.Green);
				var started = DateTime.Now;

				var storageData = new StorageAccountCreateOrUpdateContent(
					new StorageSku(StorageSkuName.StandardLrs),
					StorageKind.StorageV2,
					new AzureLocation(Program.Context.AppConfig.TargetRegionName))
				{
					EnableHttpsTrafficOnly = true,
					MinimumTlsVersion = StorageMinimumTlsVersion.Tls1_2,
					AllowBlobPublicAccess = false,
					AccessTier = StorageAccountAccessTier.Hot,
				};

				var createOperation = await storageAccounts.CreateOrUpdateAsync(
					WaitUntil.Completed,
					storageAccountName,
					storageData,
					cancellationToken);

				storageAccount = createOperation.Value;

				var blobService = (await storageAccount.GetBlobService().GetAsync(cancellationToken)).Value;
				var containers = blobService.GetBlobContainers();

				await containers.CreateOrUpdateAsync(
					WaitUntil.Completed,
					Program.Context.AppConfig.Storage.ContainerName,
					new BlobContainerData { PublicAccess = StoragePublicAccessType.None },
					cancellationToken);

				var elapsed = DateTime.Now.Subtract(started);
				ConsoleHelper.WriteLine($"{counter,3}: Created storage account: {storageAccountName} in {elapsed}", ConsoleColor.Yellow);
			}

			var accountKey = await StorageAccountHelper.GetStorageAccountKey(storageAccount, cancellationToken);

			attendeeInfo.StorageAccountConnectionString =
				$"DefaultEndpointsProtocol=https;AccountName={storageAccountName};AccountKey={accountKey};EndpointSuffix=core.windows.net";
		}

	}
}
