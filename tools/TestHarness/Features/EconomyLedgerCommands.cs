using System;
using System.Collections.Generic;
using System.Linq;
using CMS21Together.Data;
using CMS21Together.Logic.Economy;
using CMS21Together.Network;
using CMS21_Together_Core.Network.Packets;

namespace TogetherTestHarness.Features;

public static partial class EconomyCommands
{
    [HarnessCommand("econ-unattributed")]
    private static object Unattributed(string args)
    {
        string text = (args ?? "").Trim();
        if (text == "reset")
        {
            EconomyAudit.ResetUnattributed();
            return EconomySection();
        }
        if (!int.TryParse(text, out int amount) || amount == 0) throw new ArgumentException("usage: econ-unattributed <amount>|reset");
        string before = Stats();
        GlobalData.AddPlayerMoney(amount);
        return Result(before, new Dictionary<string, object> { ["unattributed"] = EconomyAudit.Unattributed });
    }

    [HarnessCommand("econ-ledger")]
    private static object Ledger(string args)
    {
        bool resent = false;
        switch ((args ?? "").Trim())
        {
            case "": break;
            case "resend": resent = EconomyRequests.Resend(); break;
            default: throw new ArgumentException("usage: econ-ledger [resend]");
        }
        var last = EconomyRequests.LastResult;
        return new Dictionary<string, object>
        {
            ["resent"] = resent,
            ["economy"] = EconomySection(),
            ["requests"] = EconomyRequests.Recent.Select(r => (object)new
            {
                id = r.RequestId, reason = r.Reason.ToString(), r.Money, r.Scraps, r.Exp, r.Arg, r.Arg2, r.ItemUid, loader = r.CarLoaderId,
            }).ToList(),
            ["lastResult"] = last == null ? null : new { id = last.RequestId, reason = last.Reason.ToString(), last.Accepted, refusal = last.Refusal.ToString(), last.Money, last.Scraps },
            ["lambdaPatches"] = FeeHooks.LambdaPatches,
        };
    }

    [HarnessCommand("econ-send")]
    private static object SendRaw(string args)
    {
        var parts = Split(args);
        if (parts.Length < 2) throw new ArgumentException("usage: econ-send <Reason> <money> [arg] [scraps] [exp] [itemUid]");
        var request = new EconomyRequestPacket
        {
            Reason = (EconomyReason)Enum.Parse(typeof(EconomyReason), parts[0], true),
            Money = int.Parse(parts[1]),
            Arg = parts.Length > 2 ? int.Parse(parts[2]) : 0,
            Scraps = parts.Length > 3 ? int.Parse(parts[3]) : 0,
            Exp = parts.Length > 4 ? int.Parse(parts[4]) : 0,
            ItemUid = parts.Length > 5 ? long.Parse(parts[5]) : 0,
        };
        int id = EconomyRequests.Send(request);
        return new { id, reason = request.Reason.ToString() };
    }

    [HarnessCommand("econ-skill-unlock")]
    private static object SkillUnlock(string args)
    {
        var parts = Split(args);
        if (parts.Length != 2) throw new ArgumentException("usage: econ-skill-unlock <skill id> <level>");
        Client.Instance.Send(new UpgradeRequest { id = parts[0], level = int.Parse(parts[1]), type = CMS21_Together_Core.Data.Enum.UpgradeType.Points });
        return "sent";
    }

    public static object EconomySection() => new Dictionary<string, object>
    {
        ["unattributed"] = EconomyAudit.Unattributed,
        ["sent"] = new Dictionary<string, int>(EconomyAudit.Sent),
        ["covered"] = new Dictionary<string, int>(EconomyAudit.CoveredCalls),
        ["suppressed"] = new Dictionary<string, int>(EconomyAudit.SuppressedCalls),
        ["refused"] = new Dictionary<string, int>(EconomyAudit.Refused),
        ["lastUnattributed"] = EconomyAudit.LastUnattributed.Select(u => (object)$"{u.UtcTime:HH:mm:ss} {u.Kind} {u.Amount} scene {u.Scene} mode {u.Mode} window {u.Window}").ToList(),
    };

    public static object SkillsSection()
    {
        try
        {
            var system = GameData.Instance?.GarageTools?.upgradeSystem;
            if (system == null || system.UpgradesForPoints == null) return null;
            var unlocked = new List<string>();
            foreach (var upgrade in system.UpgradesForPoints)
            {
                if (upgrade?.Unlocked == null) continue;
                for (int i = 0; i < upgrade.Unlocked.Length; i++)
                    if (upgrade.Unlocked[i]) unlocked.Add($"{upgrade.ID}:{i}");
            }
            unlocked.Sort(StringComparer.Ordinal);
            return new { unlocked, availablePoints = system.availablePoints };
        }
        catch (Exception ex)
        {
            return new { error = ex.Message };
        }
    }
}
