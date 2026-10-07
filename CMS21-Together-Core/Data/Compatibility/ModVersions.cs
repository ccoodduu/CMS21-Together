namespace CMS21_Together_Core.Data.Compatibility;

// Two builds of the same version and build kind ("0.6.0-dev.892" and "0.6.0-dev.894") may play together when their
// protocol hashes match; a release ("1.0.0") only plays with exactly itself.
public static class ModVersions
{
	public static bool Compatible(string a, string b)
	{
		if (a == b) return true;
		if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
		Split(a, out string baseA, out string kindA);
		Split(b, out string baseB, out string kindB);
		return kindA != null && baseA == baseB && kindA == kindB;
	}

	private static void Split(string version, out string baseVersion, out string kind)
	{
		int dash = version.IndexOf('-');
		if (dash < 0)
		{
			baseVersion = version;
			kind = null;
			return;
		}
		baseVersion = version.Substring(0, dash);
		string label = version.Substring(dash + 1);
		int dot = label.IndexOf('.');
		kind = dot < 0 ? label : label.Substring(0, dot);
	}
}
