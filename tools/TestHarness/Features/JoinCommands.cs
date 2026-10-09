using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Network;
using CMS21Together.Session;
using CMS21Together.UI;

namespace TogetherTestHarness.Features;

public static class JoinCommands
{
    [HarnessCommand("mp-join")]
    private static object Join(string args)
    {
        var options = ParseOptions(args, out string target);
        if (target.Length == 0) target = PlayerSettings.LastJoinTarget;
        options.TryGetValue("password", out string password);
        options.TryGetValue("adminkey", out string adminKey);
        if (!JoinService.Join(target, out string error, password, adminKey)) throw new ArgumentException(error);
        return $"joining {JoinService.CurrentTarget}";
    }

    [HarnessCommand("mp-kick")]
    private static object Kick(string args)
    {
        if (!int.TryParse(args?.Trim(), out int playerId)) throw new ArgumentException("usage: mp-kick <player id>");
        if (!Client.Instance.IsConnectionValid) throw new InvalidOperationException("not connected");
        Client.Instance.Send(new KickRequestPacket { PlayerId = playerId });
        return $"kick request for player {playerId} sent";
    }

    public static Dictionary<string, string> ParseOptions(string args, out string rest)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var plain = new List<string>();
        foreach (Match token in Regex.Matches(args ?? "", @"(?:[^\s""]+|""[^""]*"")+"))
        {
            string text = token.Value;
            int eq = text.IndexOf('=');
            if (eq > 0 && text[0] != '"') options[text.Substring(0, eq)] = text.Substring(eq + 1).Replace("\"", "");
            else plain.Add(text.Replace("\"", ""));
        }
        rest = string.Join(" ", plain);
        return options;
    }

    [HarnessCommand("mp-presence")]
    private static object Presence(string args)
    {
        var (joinString, reason) = RichPresence.Build(ClientData.ServerInfo, JoinService.CurrentTarget);
        return new Dictionary<string, object> { ["joinString"] = joinString, ["reason"] = reason, ["published"] = RichPresence.JoinString, ["status"] = RichPresence.StatusText };
    }

    [HarnessCommand("mp-join-string")]
    private static object JoinString(string args)
    {
        if (!JoinService.HandleJoinString(args?.Trim(), out string error)) throw new ArgumentException(error);
        return JoinService.PendingConfirmation != null ? "waiting for confirmation" : "joining";
    }

    [HarnessCommand("mp-answer")]
    private static object Answer(string args)
    {
        bool yes = string.Equals(args?.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
        JoinService.Answer(yes);
        return yes ? "leaving and joining" : "staying";
    }

    [HarnessCommand("mp-status")]
    private static object Status(string args) => Session();

    [HarnessCommand("mp-fake-version")]
    private static object FakeVersion(string args)
    {
        string value = args?.Trim();
        ClientVersion.Override = string.Equals(value, "reset", StringComparison.OrdinalIgnoreCase) ? null : value;
        return $"client version {ClientVersion.Current}";
    }

    internal static void Reset(List<string> changed)
    {
        if (ClientVersion.Override != null) changed.Add($"mp-fake-version {ClientVersion.Override}");
        ClientVersion.Override = null;
        if (JoinService.PendingConfirmation != null)
        {
            changed.Add($"pending join confirmation {JoinService.PendingConfirmation}");
            JoinService.Answer(false);
        }
        if (ConnectionStatus.MessagePending)
        {
            changed.Add($"unacknowledged join message ({ConnectionStatus.LastReason})");
            MultiplayerMenuModel.AcknowledgeMessage();
        }
        if (MultiplayerMenuModel.Panel != MenuPanel.None || MultiplayerMenuModel.SessionPanelOpen)
        {
            changed.Add($"mp-ui panel {MultiplayerMenuModel.Panel}{(MultiplayerMenuModel.SessionPanelOpen ? " + session" : "")}");
            MultiplayerMenuModel.Close();
            MultiplayerMenuModel.CloseSessionPanel();
        }
        if (ModNotify.History.Count > 0) changed.Add($"toast history ({ModNotify.History.Count})");
        ModNotify.History.Clear();
    }

    [HarnessCommand("mp-ui")]
    private static object Ui(string args)
    {
        switch ((args ?? "").Trim().ToLowerInvariant())
        {
            case "open join":
                MultiplayerMenuModel.OpenJoinPanel();
                break;
            case "open host":
                MultiplayerMenuModel.OpenHostPanel();
                break;
            case "open friends":
                MultiplayerMenuModel.OpenFriendsPanel();
                break;
            case "open session":
                MultiplayerMenuModel.OpenSessionPanel();
                break;
            case "close session":
                MultiplayerMenuModel.CloseSessionPanel();
                break;
            case "close":
                MultiplayerMenuModel.Close();
                break;
            case "ok":
                MultiplayerMenuModel.AcknowledgeMessage();
                break;
            default:
                throw new ArgumentException("usage: mp-ui open join|open host|open friends|open session|close session|close|ok");
        }
        return Session();
    }

    [HarnessCommand("mp-players")]
    private static object Players(string args) => PlayerRows();

    private static List<Dictionary<string, object>> PlayerRows() => MultiplayerMenuModel.PlayerRows().Select(r => new Dictionary<string, object>
    {
        ["id"] = r.Id,
        ["name"] = r.Name,
        ["scene"] = r.Scene,
        ["pingMs"] = r.PingMs,
        ["local"] = r.IsLocal,
        ["kickable"] = MultiplayerMenuModel.IsAdmin && !r.IsLocal,
    }).ToList();

    [HarnessCommand("mp-host")]
    private static object Host(string args)
    {
        var options = ParseOptions(args, out string action);
        switch (action.ToLowerInvariant())
        {
            case "start":
                var settings = new HostSettings();
                if (options.TryGetValue("port", out string port)) settings.Port = int.Parse(port);
                if (options.TryGetValue("maxPlayers", out string maxPlayers)) settings.MaxPlayers = int.Parse(maxPlayers);
                if (options.TryGetValue("password", out string password)) settings.Password = password;
                if (options.TryGetValue("steam", out string steam)) settings.UseSteam = bool.Parse(steam);
                if (options.TryGetValue("difficulty", out string difficulty)) settings.Difficulty = (Gamemode)Enum.Parse(typeof(Gamemode), difficulty, true);
                if (options.TryGetValue("new", out string startOver)) settings.StartOver = bool.Parse(startOver);
                options.TryGetValue("serverPath", out string serverPath);
                if (!LocalServerHost.Start(settings, out string error, serverPath)) throw new InvalidOperationException(error);
                break;
            case "stop":
                LocalServerHost.Stop();
                break;
            case "stop-leftover":
                options.TryGetValue("serverPath", out string leftoverPath);
                LocalServerHost.StopLeftover(leftoverPath);
                break;
            case "status":
                break;
            default:
                throw new ArgumentException("usage: mp-host start [serverPath=..] [port=..] [difficulty=..] [password=..] [maxPlayers=..] [steam=..] [new=true] | stop | stop-leftover [serverPath=..] | status");
        }
        return HostStatus();
    }

    public static Dictionary<string, object> HostStatus() => new Dictionary<string, object>
    {
        ["state"] = LocalServerHost.State.ToString(),
        ["message"] = LocalServerHost.Message,
        ["logTail"] = LocalServerHost.LogTail,
        ["port"] = LocalServerHost.Port,
        ["serverPath"] = LocalServerHost.RunningServerPath ?? LocalServerHost.ServerPath,
        ["hasSave"] = LocalServerHost.HasSave(LocalServerHost.RunningServerPath ?? LocalServerHost.ServerPath),
        ["leftoverRunning"] = LocalServerHost.LeftoverRunning,
        ["adminKeySet"] = LocalServerHost.AdminKey != null,
    };

    private static string GameDifficulty()
    {
        var manager = Singleton<GameManager>.Instance;
        var difficulty = manager == null ? null : manager.DifficultyManager;
        return difficulty == null ? null : difficulty.GetDifficultyLevel().ToString();
    }

    public static Dictionary<string, object> LastDisconnect() => new Dictionary<string, object>
    {
        ["reason"] = ConnectionStatus.LastReason,
        ["message"] = ConnectionStatus.Message,
    };

    public static Dictionary<string, object> Session()
    {
        var info = ClientData.ServerInfo;
        return new Dictionary<string, object>
        {
            ["joinStatus"] = ConnectionStatus.State.ToString(),
            ["lastDisconnect"] = LastDisconnect(),
            ["messagePending"] = ConnectionStatus.MessagePending,
            ["target"] = JoinService.CurrentTarget?.ToString(),
            ["serverInfo"] = info == null ? null : new Dictionary<string, object>
            {
                ["name"] = info.ServerName,
                ["modVersion"] = info.ModVersion,
                ["port"] = info.Port,
                ["maxPlayers"] = info.MaxPlayers,
                ["steamId"] = info.SteamId.ToString(),
                ["difficulty"] = info.Difficulty.ToString(),
                ["sharedDlc"] = info.SharedDlc ?? new List<string>(),
                ["isAdmin"] = info.IsAdmin,
                ["passwordRequired"] = info.PasswordRequired,
            },
            ["gameDifficulty"] = GameDifficulty(),
            ["pings"] = ClientData.PlayerPings.ToDictionary(p => p.Key.ToString(), p => (object)p.Value),
            ["toasts"] = ModNotify.History.Select(t => t.Text).ToList(),
            ["messages"] = ModNotify.Messages.Select(m => $"{m.Title}: {m.Text}").ToList(),
            ["panel"] = MultiplayerMenuModel.Panel.ToString().ToLowerInvariant(),
            ["host"] = HostStatus(),
            ["sessionPanel"] = MultiplayerMenuModel.SessionPanelOpen,
            ["players"] = PlayerRows(),
            ["passwordField"] = MultiplayerMenuModel.PasswordFieldShown,
            ["friends"] = new Dictionary<string, object>
            {
                ["available"] = SteamFriendsList.Available,
                ["status"] = SteamFriendsList.Status,
                ["count"] = SteamFriendsList.Rows.Count,
                ["joinable"] = SteamFriendsList.Rows.Count(r => r.CanJoin),
            },
            ["panelError"] = MultiplayerMenuModel.PanelError,
            ["pendingConfirmation"] = JoinService.PendingConfirmation?.ToString(),
        };
    }
}
