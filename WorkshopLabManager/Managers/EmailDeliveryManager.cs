using WorkshopLabManager.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WorkshopLabManager.Managers
{
	public static class EmailDeliveryManager
	{
		public static async Task EmailAttendees(string attendeeName)
		{
			var emailConfig = Program.Context.AppConfig.Email;
			var sourceAttendees = attendeeName == null ? Program.Context.Attendees : [new AttendeeInfo(attendeeName)];

			if (!ConsoleHelper.ConfirmYesNo($"Are you sure you want to email {sourceAttendees.Length} attendee(s)?"))
			{
				return;
			}

			var attendees = LoadAttendeeResources();
			var sent = 0;
			var skipped = 0;

			using var smtp = new SmtpClient(emailConfig.SmtpHost, emailConfig.SmtpPort)
			{
				EnableSsl = true,
				Credentials = new NetworkCredential(emailConfig.SmtpUsername, emailConfig.SmtpPassword),
			};

			ConsoleHelper.WriteLine();

			foreach (var sourceAttendee in sourceAttendees.OrderBy(a => a.AttendeeName))
			{
				var attendee = attendees.GetValueOrDefault(sourceAttendee.AttendeeName);
				if (attendee == null)
				{
					ConsoleHelper.WriteLine($"Skipping '{sourceAttendee.AttendeeName}': no resources found.", ConsoleColor.DarkGreen);
					skipped++;
					continue;
				}

				if (string.IsNullOrWhiteSpace(attendee.EmailAddress))
				{
					ConsoleHelper.WriteLine($"Skipping '{sourceAttendee.AttendeeName}': no email address.", ConsoleColor.DarkGreen);
					skipped++;
					continue;
				}

				var publicIpAddress = await VirtualMachineHelper.GetIpAddressAsync(attendee, cancellationToken: CancellationToken.None);

				using var msg = new MailMessage
				{
					From = new MailAddress(emailConfig.SmtpUsername, emailConfig.FromDisplayName),
					Subject = $"Your Personalized Lab Resources for {Program.Context.AppConfig.WorkshopName}",
					Body = BuildHtmlEmailBody(attendee, publicIpAddress),
					IsBodyHtml = true
				};

				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write("Emailing ");
				Console.Write(attendee.AttendeeName);
				if (emailConfig.EnableTestRecipient)
				{
					msg.To.Add(new MailAddress(emailConfig.TestRecipient, attendee.AttendeeName));
				}
				else
				{
					msg.To.Add(new MailAddress(attendee.EmailAddress, attendee.AttendeeName));
				}
				Console.Write(" ");
				Console.ForegroundColor = ConsoleColor.Cyan;
				if (emailConfig.EnableTestRecipient)
				{
					Console.Write($"{emailConfig.TestRecipient} (for {attendee.EmailAddress})");
				}
				else
				{
					Console.Write(attendee.EmailAddress);
				}
				Console.Write(" ");

				try
				{
					await smtp.SendMailAsync(msg);
					Console.ForegroundColor = ConsoleColor.White;
					Console.Write("SENT");
					sent++;
				}
				catch (Exception ex)
				{
					Console.ForegroundColor = ConsoleColor.Red;
					Console.Write($"ERROR: {ex.Message}");
				}
				finally
				{
					Console.ResetColor();
					ConsoleHelper.WriteLine();
				}
			}

			ConsoleHelper.WriteLine($"Total emails sent: {sent}, skipped: {skipped}");
		}

		private static Dictionary<string, AttendeeInfo> LoadAttendeeResources()
		{
			const int AttendeeNameColumnIndex = 0;
			const int EmailAddressColumnIndex = 1;
			const int SqlDatabaseServerNameColumnIndex = 2;
			const int EventHubNamespaceNameColumnIndex = 3;
			const int EventHubSasTokenColumnIndex = 4;
			const int StorageAccountConnectionStringColumnIndex = 5;

			var currentDir = AppContext.BaseDirectory + "\\..\\..\\..";
			var path = Path.Combine(currentDir, "AttendeeResources.csv");

			var dict = new Dictionary<string, AttendeeInfo>(StringComparer.OrdinalIgnoreCase);
			var lines = File.ReadAllLines(path);
			if (lines.Length <= 1)
			{
				return dict;
			}

			foreach (var line in lines.Skip(1))
			{
				if (string.IsNullOrWhiteSpace(line))
				{
					continue;
				}

				var parts = line.Split(',');

				var info = new AttendeeInfo(parts[AttendeeNameColumnIndex].Trim(), parts[EmailAddressColumnIndex].Trim())
				{
					SqlDatabaseServerName = parts[SqlDatabaseServerNameColumnIndex].Trim(),
					EventHubNamespaceName = parts[EventHubNamespaceNameColumnIndex].Trim(),
					EventHubSasToken = parts[EventHubSasTokenColumnIndex].Trim(),
					StorageAccountConnectionString = parts[StorageAccountConnectionStringColumnIndex].Trim()
				};

				dict[info.AttendeeName] = info;
			}
			return dict;
		}

		private static string BuildHtmlEmailBody(AttendeeInfo attendee, string publicIpAddress) => $@"
			<!DOCTYPE html>
			<html>
				<body style=""font-family:Arial; font-size: 12pt;"">
					<p>Hello {attendee.AttendeeName},</p>
					<p>Welcome to <b>{Program.Context.AppConfig.WorkshopName}</b>! Here are your personalized lab resources for today's workshop.</p>
					{FormatHtmlEmailHeading("Virtual Machine")}
					{FormatHtmlEmailProperty("Remote Desktop", $"mstsc /v:{publicIpAddress}")}
					{FormatHtmlEmailProperty("Username", $".\\{Program.Context.AppConfig.VirtualMachine.Credentials.AdminUsername}")}
					{FormatHtmlEmailProperty("Password", Program.Context.AppConfig.VirtualMachine.Credentials.AdminPassword)}
					{FormatHtmlEmailHeading("Event Hub")}
					{FormatHtmlEmailProperty("Event Hub Namespace Name", attendee.EventHubNamespaceName)}
					{FormatHtmlEmailProperty("Event Hub SAS Token", attendee.EventHubSasToken)}
					{FormatHtmlEmailHeading("Storage")}
					{FormatHtmlEmailProperty("Storage Connection String", attendee.StorageAccountConnectionString)}
					{FormatHtmlEmailHeading("OpenAI")}
					{FormatHtmlEmailProperty("OpenAI API Key", Program.Context.AppConfig.OpenAI.ApiKey)}
					<p>Enjoy your day of learning!</p>
				</body>
			</html>";

		private static string FormatHtmlEmailHeading(string text) =>
			$@"<p style=""margin-top: 16px; margin-bottom: 4px; font-size: 14pt;""><b>{text}</b></p>";

		private static string FormatHtmlEmailProperty(string key, string value) =>
			$@"
				<p style=""margin-top: 4px; margin-bottom: 4px; font-size: 12pt; margin-left: 8px;"">&bull;&nbsp;&nbsp;<b>{key}</b></p>
				<p style=""margin-top: 4px; margin-bottom: 8px; font-size: 12pt; margin-left: 32px;"">{value}</p>
			";

	}
}
