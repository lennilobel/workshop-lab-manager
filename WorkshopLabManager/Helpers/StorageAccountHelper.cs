using Azure.ResourceManager.Storage;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WorkshopLabManager.Helpers
{
	public class StorageAccountHelper
	{
		public static string BuildStorageAccountName(string attendeeNameIdentifier)
		{
			var rawStorageAccountName = $"{Program.Context.AppConfig.Storage.AccountName}-{attendeeNameIdentifier}";
			var safeStorageAccountName = new string(rawStorageAccountName.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
			var storageAccountName = safeStorageAccountName.Length > 24 ? safeStorageAccountName.Substring(0, 24) : safeStorageAccountName;

			return storageAccountName;
		}

		public static async Task<string> GetStorageAccountKey(StorageAccountResource storageAccount, CancellationToken cancellationToken)
		{
			var response = await storageAccount.GetKeysAsync(cancellationToken: cancellationToken);
			var storageAccountKey = response.Value.Keys[0].Value;

			return storageAccountKey;
		}

	}
}
