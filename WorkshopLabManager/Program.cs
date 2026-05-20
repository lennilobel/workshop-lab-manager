using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.Resources;
using WorkshopLabManager.Helpers;
using WorkshopLabManager.Managers;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace WorkshopLabManager
{
	public class Program
	{
		public static Context Context { get; private set; }

		static async Task Main()
		{
			DisplayHeading();

			await InitializeApplication();

			if (Context == null)
			{
				Console.WriteLine("Application initialization failed. Please check application configuration.");
				return;
			}

			Console.Clear();

			var action = default(string);
			do
			{
				DisplayHeading();
				ConsoleHelper.WriteLine();
				ConsoleHelper.WriteLine("Choose an action", ConsoleColor.White);
				ConsoleHelper.WriteLine($"  V = View configuration");
				ConsoleHelper.WriteLine($"  A = Show attendee list");
				ConsoleHelper.WriteLine($"  L = List all attendee resources");
				ConsoleHelper.WriteLine($"  C = Create attendee resources");
				ConsoleHelper.WriteLine($"  D = Delete attendee resources");
				ConsoleHelper.WriteLine($"  E = Email attendees");
				ConsoleHelper.WriteLine($"  P = Publish source VM and replicate to target region(s)");
				ConsoleHelper.WriteLine($"  Q = Quit");
				ConsoleHelper.WriteLine();
				Console.Write("Enter choice: ");

				var input = Console.ReadLine()?.Trim();
				var tokens = input?.Split([' '], 2, StringSplitOptions.RemoveEmptyEntries) ?? [];
				action = tokens.ElementAtOrDefault(0)?.ToUpperInvariant();
				var attendeeName = tokens.ElementAtOrDefault(1); // attendee name if provided, or null attendee name for all attendees
				attendeeName = string.IsNullOrWhiteSpace(attendeeName) ? null : attendeeName;

				switch (action)
				{
					case "V":
						LabConfigurationManager.ViewConfiguration();
						break;

					case "A":
						LabConfigurationManager.ShowAttendeeList();
						break;

					case "L":
						await ResourceListManager.ListResources();
						break;

					case "C":
						await ResourceCreationManager.CreateResources(attendeeName);
						break;

					case "D":
						await ResourceDeletionManager.DeleteResources(attendeeName);
						break;

					case "E":
						await EmailDeliveryManager.EmailAttendees(attendeeName);
						break;

					case "P":
						await VirtualMachinePublishManager.PublishAndReplicate();
						break;

					case "Q":
						ConsoleHelper.WriteLine("Exiting...");
						break;

					default:
						ConsoleHelper.WriteLine("Invalid input.");
						break;
				}
				ConsoleHelper.WriteLine();
				Console.Write("Press any key to continue... ");
				Console.ReadKey(intercept: true);
				Console.Clear();

			} while (action != "Q");
		}

		private static void DisplayHeading() =>
			ConsoleHelper.WriteLine($"Workshop Lab Manager", ConsoleColor.Cyan);

		private static async Task InitializeApplication()
		{
			ConsoleHelper.WriteLine();
			ConsoleHelper.WriteLine("Initializing...");

			var currentDir = AppContext.BaseDirectory + "\\..\\..\\..";
			var attendeesFilePath = Path.Combine(currentDir, "Attendees.csv");

			if (!File.Exists(attendeesFilePath))
			{
				ConsoleHelper.WriteLine($"Attendee list file not found at '{attendeesFilePath}'", ConsoleColor.Red);
				return;
			}

			var appConfig = LabConfigurationManager.LoadConfiguration(includeUserSecrets: true);

			var attendees = File.ReadAllLines(attendeesFilePath)
				.Select(line => line.Trim())
				.Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("#"))
				.Select(line =>
				{
					var parts = line.Split(',', 2);
					var name = parts[0].Trim();
					var email = parts.Length > 1 ? parts[1].Trim() : string.Empty;
					return new AttendeeInfo(name, email);
				})
				.ToArray();

			var credential = new AzureCliCredential(); // requires `az login`
			var armClient = new ArmClient(credential);

			var subscriptionData = default(SubscriptionData);
			var sourceResourceGroup = default(ResourceGroupResource);
			var targetResourceGroup = default(ResourceGroupResource);
			try
			{
				var subscription = await armClient.GetDefaultSubscriptionAsync();
				subscriptionData = subscription.Data;

				sourceResourceGroup = await subscription.GetResourceGroups().GetAsync(appConfig.SourceResourceGroupName);
				targetResourceGroup = await subscription.GetResourceGroups().GetAsync(appConfig.TargetResourceGroupName);
			}
			catch (Exception ex)
			{
				ConsoleHelper.WriteLine("Could not retrieve Azure resource information.", ConsoleColor.Red);
				ConsoleHelper.WriteLine(ex.Message, ConsoleColor.Red);
				return;
			}

			Context = new Context(
				appConfig,
				attendees,
				subscriptionData,
				sourceResourceGroup,
				targetResourceGroup
			);
		}

	}
}
