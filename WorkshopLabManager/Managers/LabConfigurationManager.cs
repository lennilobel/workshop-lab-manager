using Microsoft.Extensions.Configuration;
using WorkshopLabManager.Helpers;
using System;
using System.IO;

namespace WorkshopLabManager.Managers
{
	public static class LabConfigurationManager
	{
		public static AppConfig LoadConfiguration(bool includeUserSecrets)
		{
			/*
				%APPDATA%\Microsoft\UserSecrets\

				dotnet user-secrets init
				dotnet user-secrets set "AppConfig:OpenAI:ApiKey" "<api-key>"
				dotnet user-secrets set "AppConfig:Email:SmtpHost" "<smtp-host>"
				dotnet user-secrets set "AppConfig:Email:SmtpUsername" "<smtp-username>"
				dotnet user-secrets set "AppConfig:Email:SmtpPassword" "<smtp-password>"
			*/

			var builder = new ConfigurationBuilder()
				.SetBasePath(Directory.GetCurrentDirectory())
				.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

			if (includeUserSecrets)
			{
				builder.AddUserSecrets<Program>(optional: false);
			}

			var appConfig = builder.Build()
				.GetRequiredSection("AppConfig")
				.Get<AppConfig>();

			return appConfig;
		}

		public static void ViewConfiguration()
		{
			var appConfig = LoadConfiguration(includeUserSecrets: false);

			ConsoleHelper.WriteLine();
			ConsoleHelper.WriteLine("Azure Subscription Information", ConsoleColor.White);
			ConsoleHelper.WriteLine($"  Subscription Name      {Program.Context.SubscriptionData.DisplayName}");
			ConsoleHelper.WriteLine($"  Subscription ID        {Program.Context.SubscriptionData.SubscriptionId}");
			ConsoleHelper.WriteLine($"  Tenant ID              {Program.Context.SubscriptionData.TenantId}");
			ConsoleHelper.WriteLine();
			ConsoleHelper.WriteLine("Primary Configuration", ConsoleColor.White);
			ConsoleHelper.WriteLine($"  Workshop Name          {appConfig.WorkshopName}");
			ConsoleHelper.WriteLine($"  Source Resource Group  {appConfig.SourceResourceGroupName}");
			ConsoleHelper.WriteLine($"  Target Resource Group  {appConfig.TargetResourceGroupName}");
			ConsoleHelper.WriteLine($"  Target Region Name     {appConfig.TargetRegionName}");
			ConsoleHelper.WriteLine();

			if (appConfig.VirtualMachine.IsEnabled)
			{
				ConsoleHelper.WriteLine($"Virtual Machine", ConsoleColor.White);
				ConsoleHelper.WriteLine($"  Publish", ConsoleColor.White);
				ConsoleHelper.WriteLine($"    Source VM Name       {appConfig.VirtualMachine.Publish.SourceVmName}");
				ConsoleHelper.WriteLine($"    Snapshot Name        {appConfig.VirtualMachine.Publish.SnapshotName}");
				ConsoleHelper.WriteLine($"    Gallery Name         {appConfig.VirtualMachine.Publish.GalleryName}");
				ConsoleHelper.WriteLine($"    Image Name           {appConfig.VirtualMachine.Publish.ImageName}");
				ConsoleHelper.WriteLine($"    Image Version        {appConfig.VirtualMachine.Publish.ImageVersion}");
				ConsoleHelper.WriteLine($"    Target Region Names  {string.Join(", ", appConfig.VirtualMachine.Publish.TargetRegionNames)}");
				ConsoleHelper.WriteLine($"  Clone", ConsoleColor.White);
				ConsoleHelper.WriteLine($"    VM Size              {appConfig.VirtualMachine.Clone.VmSize}");
				ConsoleHelper.WriteLine($"    Max VMs per Region   {appConfig.VirtualMachine.Clone.MaxVmsPerRegion}");
				ConsoleHelper.WriteLine($"    VM Name Prefix       {appConfig.VirtualMachine.Clone.VmNamePrefix}");
				ConsoleHelper.WriteLine($"  Credentials", ConsoleColor.White);
				ConsoleHelper.WriteLine($"    Admin Username       {appConfig.VirtualMachine.Credentials.AdminUsername}");
				ConsoleHelper.WriteLine($"    Admin Password       {appConfig.VirtualMachine.Credentials.AdminPassword}");
				ConsoleHelper.WriteLine();
			}

			if (appConfig.SqlDatabase.IsEnabled)
			{
				ConsoleHelper.WriteLine($"SQL Database", ConsoleColor.White);
				ConsoleHelper.WriteLine($"  Server Name            {appConfig.SqlDatabase.ServerName}");
				ConsoleHelper.WriteLine($"  Username               {appConfig.SqlDatabase.Username}");
				ConsoleHelper.WriteLine($"  Password               {appConfig.SqlDatabase.Password}");
				ConsoleHelper.WriteLine($"  Database SKU           {appConfig.SqlDatabase.DatabaseSku}");
				ConsoleHelper.WriteLine($"  AdventureWorks", ConsoleColor.White);
				ConsoleHelper.WriteLine($"    Source Server Name   {appConfig.SqlDatabase.AdventureWorks.SourceServerName}");
				ConsoleHelper.WriteLine($"    Database Name        {appConfig.SqlDatabase.AdventureWorks.DatabaseName}");
				ConsoleHelper.WriteLine();
			}

			if (appConfig.EventHub.IsEnabled)
			{
				ConsoleHelper.WriteLine($"Event Hub", ConsoleColor.White);
				ConsoleHelper.WriteLine($"  Namespace Name         {appConfig.EventHub.NamespaceName}");
				ConsoleHelper.WriteLine($"  Event Hub Name         {appConfig.EventHub.EventHubName}");
				ConsoleHelper.WriteLine($"  Policy Name            {appConfig.EventHub.PolicyName}");
				ConsoleHelper.WriteLine($"  SAS Token Expiration   {appConfig.EventHub.SasTokenExpirationDays} day(s)");
				ConsoleHelper.WriteLine();
			}

			if (appConfig.Storage.IsEnabled)
			{
				ConsoleHelper.WriteLine($"Storage", ConsoleColor.White);
				ConsoleHelper.WriteLine($"  Account Name           {appConfig.Storage.AccountName}");
				ConsoleHelper.WriteLine($"  Container Name         {appConfig.Storage.ContainerName}");
				ConsoleHelper.WriteLine();
			}

			ConsoleHelper.WriteLine($"OpenAI", ConsoleColor.White);
			ConsoleHelper.WriteLine($"  API Key                {appConfig.OpenAI.ApiKey}");
			ConsoleHelper.WriteLine();
			ConsoleHelper.WriteLine($"Email", ConsoleColor.White);
			ConsoleHelper.WriteLine($"  SMTP Host              {appConfig.Email.SmtpHost}");
			ConsoleHelper.WriteLine($"  SMTP Port              {appConfig.Email.SmtpPort}");
			ConsoleHelper.WriteLine($"  SMTP Username          {appConfig.Email.SmtpUsername}");
			ConsoleHelper.WriteLine($"  SMTP Password          {appConfig.Email.SmtpPassword}");
			ConsoleHelper.WriteLine($"  From Display Name      {appConfig.Email.FromDisplayName}");
			ConsoleHelper.WriteLine($"  Test Recipient         {appConfig.Email.TestRecipient}");
			ConsoleHelper.WriteLine($"  Enable Test Recipient  {appConfig.Email.EnableTestRecipient}");
			ConsoleHelper.WriteLine();
		}

		public static void ShowAttendeeList()
		{
			ConsoleHelper.WriteLine();
			ConsoleHelper.WriteLine("Attendee list:");
			ConsoleHelper.WriteLine();

			foreach (var attendee in Program.Context.Attendees)
			{
				var name = $"{attendee.AttendeeName} ({attendee.AttendeeNameIdentifier})";
				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write($"{name,-40}");
				Console.ForegroundColor = ConsoleColor.Cyan;
				Console.Write(attendee.EmailAddress);
				Console.ResetColor();
				ConsoleHelper.WriteLine();
			}

			ConsoleHelper.WriteLine();
			ConsoleHelper.WriteLine($"Total Attendees: {Program.Context.Attendees.Length}");
		}

	}
}
