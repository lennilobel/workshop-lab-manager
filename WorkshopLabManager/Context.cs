using Azure.ResourceManager.Resources;

namespace WorkshopLabManager
{
	public class Context
	{
		public AppConfig AppConfig { get; }
		public AttendeeInfo[] Attendees { get; }
		public SubscriptionData SubscriptionData { get; }
		public ResourceGroupResource SourceResourceGroup { get; }
		public ResourceGroupResource TargetResourceGroup { get; }

		public Context(
			AppConfig appConfig,
			AttendeeInfo[] attendees,
			SubscriptionData subscriptionData,
			ResourceGroupResource sourceResourceGroup,
			ResourceGroupResource targetResourceGroup)
		{
			this.AppConfig = appConfig;
			this.Attendees = attendees;
			this.SubscriptionData = subscriptionData;
			this.SourceResourceGroup = sourceResourceGroup;
			this.TargetResourceGroup = targetResourceGroup;
		}
	}
}
