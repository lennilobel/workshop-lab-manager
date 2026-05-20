namespace WorkshopLabManager
{
	public class AppConfig
	{
		public string WorkshopName { get; init; }
		public string SourceResourceGroupName { get; init; }
		public string TargetResourceGroupName { get; init; }
		public string TargetRegionName { get; init; }

		public VirtualMachineConfig VirtualMachine { get; init; }
		public class VirtualMachineConfig
		{
			public bool IsEnabled { get; init; }
			public PublishConfig Publish { get; init; }
			public class PublishConfig
			{
				public string SourceVmName { get; init; }
				public string SnapshotName { get; init; }
				public string GalleryName { get; init; }
				public string ImageName { get; init; }
				public string ImageVersion { get; init; }
				public string[] TargetRegionNames { get; init; }
			}

			public CloneConfig Clone { get; init; }
			public class CloneConfig
			{
				public string VmSize { get; init; }
				public int MaxVmsPerRegion { get; init; }
				public string VmNamePrefix { get; init; }
			}

			public CredentialsConfig Credentials { get; init; }
			public class CredentialsConfig
			{
				public string AdminUsername { get; init; }
				public string AdminPassword { get; init; }
			}
		}

		public SqlDatabaseConfig SqlDatabase { get; init; }
		public class SqlDatabaseConfig
		{
			public bool IsEnabled { get; init; }
			public string ServerName { get; init; }
			public string Username { get; init; }
			public string Password { get; init; }
			public string DatabaseSku { get; init; }
			public AdventureWorksConfig AdventureWorks { get; init; }
			public class AdventureWorksConfig
			{
				public string SourceServerName { get; init; }
				public string DatabaseName { get; init; }
			}
		}

		public EventHubConfig EventHub { get; init; }
		public class EventHubConfig
		{
			public bool IsEnabled { get; init; }
			public string NamespaceName { get; init; }
			public string EventHubName { get; init; }
			public string PolicyName { get; init; }
			public int SasTokenExpirationDays { get; init; }
		}

		public StorageConfig Storage { get; init; }
		public class StorageConfig
		{
			public bool IsEnabled { get; init; }
			public string AccountName { get; init; }
			public string ContainerName { get; init; }
		}

		public OpenAIConfig OpenAI { get; init; }
		public class OpenAIConfig
		{
			public string ApiKey { get; init; }
		}

		public EmailConfig Email { get; init; }
		public class EmailConfig
		{
			public string SmtpHost { get; init; }
			public int SmtpPort { get; init; }
			public string SmtpUsername { get; init; }
			public string SmtpPassword { get; init; }
			public string FromDisplayName { get; init; }
			public string TestRecipient { get; init; }
			public bool EnableTestRecipient { get; init; }
		}
	}
}
