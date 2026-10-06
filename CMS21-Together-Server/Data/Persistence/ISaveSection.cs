using Newtonsoft.Json.Linq;

namespace CMS21_Together_Server.Data.Persistence
{
	public interface ISaveSection
	{
		string Key { get; }
		int Version { get; }
		JToken Save();
		void Load(JToken data);
		void Reset();
		JToken Migrate(JToken data, int fromVersion);
	}
}
