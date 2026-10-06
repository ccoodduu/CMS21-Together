using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace CMS21Together.Session;

public enum JoinTargetKind
{
	Ip,
	Steam
}

public class JoinTarget
{
	public const string JoinStringPrefix = "CMS21Together:";

	private static readonly Regex HostName = new Regex(@"^[A-Za-z0-9]([A-Za-z0-9\-\.]*[A-Za-z0-9])?$");
	private static readonly Regex SteamId = new Regex(@"^\d{17}$");

	public JoinTargetKind Kind { get; private set; }
	public string Host { get; private set; }
	public int Port { get; private set; }
	public ulong SteamServerId { get; private set; }

	public string Address => $"{Host}:{Port}";

	public override string ToString() => Kind == JoinTargetKind.Steam ? SteamServerId.ToString() : Address;

	public string ToJoinString() => Kind == JoinTargetKind.Steam
		? $"{JoinStringPrefix}steam:{SteamServerId}"
		: $"{JoinStringPrefix}ip:{Host}:{Port}";

	public static JoinTarget Ip(string host, int port) => new JoinTarget { Kind = JoinTargetKind.Ip, Host = host, Port = port };

	public static JoinTarget Steam(ulong id) => new JoinTarget { Kind = JoinTargetKind.Steam, SteamServerId = id };

	public static bool TryParse(string text, int defaultPort, out JoinTarget target, out string error)
	{
		target = null;
		error = null;
		string value = (text ?? "").Trim();
		if (value.Length == 0)
		{
			error = "Enter an IP address, a host name or a Steam server ID.";
			return false;
		}

		if (value.StartsWith(JoinStringPrefix, System.StringComparison.OrdinalIgnoreCase))
		{
			string rest = value.Substring(JoinStringPrefix.Length);
			if (rest.StartsWith("steam:", System.StringComparison.OrdinalIgnoreCase)) value = rest.Substring("steam:".Length);
			else if (rest.StartsWith("ip:", System.StringComparison.OrdinalIgnoreCase)) value = rest.Substring("ip:".Length);
			else
			{
				error = $"Unknown join string '{text}'.";
				return false;
			}
		}

		if (SteamId.IsMatch(value))
		{
			target = Steam(ulong.Parse(value));
			return true;
		}

		if (value.Count(c => c == ':') > 1 || value.Contains("["))
		{
			error = "IPv6 addresses are not supported; the server listens on IPv4 only.";
			return false;
		}

		string host = value;
		int port = defaultPort;
		int colon = value.IndexOf(':');
		if (colon >= 0)
		{
			host = value.Substring(0, colon);
			if (!int.TryParse(value.Substring(colon + 1), out port) || port < 1 || port > 65535)
			{
				error = $"'{value.Substring(colon + 1)}' is not a valid port.";
				return false;
			}
		}

		if (!IPAddress.TryParse(host, out _) && !HostName.IsMatch(host))
		{
			error = $"'{host}' is not a valid IP address or host name.";
			return false;
		}

		target = Ip(host, port);
		return true;
	}
}
