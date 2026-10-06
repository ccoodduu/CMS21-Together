namespace CMS21Together.Session;

public static class ClientVersion
{
	public static string Override { get; set; }

	public static string Current => string.IsNullOrWhiteSpace(Override) ? MainMod.ASSEMBLY_MOD_VERSION : Override;
}
