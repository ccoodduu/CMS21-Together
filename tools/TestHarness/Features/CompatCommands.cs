using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data.Compatibility;
using CMS21Together.Compatibility;
using CMS21Together.Network;
using UnityEngine;

namespace TogetherTestHarness.Features;

public static class CompatCommands
{
    [HarnessCommand("compat-report")]
    private static object Report(string args)
    {
        var mods = ConnectPacketFactory.CollectMods();
        var verdicts = new ModClassifier(ModClassifierRules.Default).ClassifyAll(mods);
        var report = new Dictionary<string, object>
        {
            ["gameVersion"] = LocalEnvironment.GameVersion,
            ["buildVersion"] = LocalEnvironment.BuildVersion,
            ["applicationVersion"] = Application.version,
            ["dlc"] = LocalEnvironment.ReadDlc()?.Select(d => new Dictionary<string, object>
            {
                ["index"] = d.Index,
                ["productId"] = d.ProductId,
                ["name"] = d.Name,
                ["owned"] = d.Owned,
            }).ToList(),
            ["ownedDlc"] = LocalEnvironment.OwnedDlc,
            ["protocolHash"] = LocalEnvironment.ProtocolHashValue,
            ["protocolHashSent"] = LocalEnvironment.SentProtocolHash,
            ["overrides"] = Overrides(),
            ["mods"] = verdicts.Select(v => new Dictionary<string, object>
            {
                ["name"] = v.Mod.Name,
                ["version"] = v.Mod.Version,
                ["file"] = v.Mod.File,
                ["assembly"] = v.Mod.Assembly,
                ["class"] = v.Class.ToString(),
                ["knownReason"] = v.KnownReason,
                ["reasons"] = v.Reasons,
                ["targets"] = v.Mod.Targets.Select(t => $"{t.Assembly}:{t.Type}.{t.Method}").ToList(),
            }).ToList(),
        };
        if ((args ?? "").Trim() == "patches")
        {
            report["melons"] = ModInventory.DescribeMelons();
            report["patches"] = ModInventory.DescribePatches().Select(p => $"{p.Kind} {p.TargetAssembly}:{p.Target} by {p.Owner} ({p.PatchAssembly})").ToList();
        }
        return report;
    }

    [HarnessCommand("compat-override")]
    private static object Override(string args)
    {
        var parts = (args ?? "").Trim().Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
        string key = parts.Length > 0 ? parts[0].ToLowerInvariant() : "";
        string value = parts.Length > 1 ? parts[1].Trim() : "";
        switch (key)
        {
            case "game":
                CompatOverrides.GameVersion = value;
                break;
            case "dlc":
                CompatOverrides.Dlc = value.Split(',').Select(d => d.Trim()).Where(d => d.Length > 0 && d != "none").ToList();
                break;
            case "protocol":
                CompatOverrides.Protocol = value;
                break;
            case "protocol-sent":
                CompatOverrides.ProtocolSent = value;
                break;
            case "mod-add":
                var mod = value.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (mod.Length != 2 || !mod[1].Contains(".")) throw new ArgumentException("usage: compat-override mod-add <name> <Type.Method>");
                int dot = mod[1].LastIndexOf('.');
                CompatOverrides.ExtraMods.Add(new ModReport
                {
                    Name = mod[0],
                    Version = "1.0",
                    Author = "harness",
                    File = mod[0] + ".dll",
                    Assembly = mod[0],
                    Targets = { new PatchTarget("Assembly-CSharp-firstpass", mod[1].Substring(0, dot), mod[1].Substring(dot + 1)) },
                });
                break;
            case "mod-clear":
                CompatOverrides.ExtraMods.Clear();
                break;
            case "reset":
                CompatOverrides.Reset();
                break;
            default:
                throw new ArgumentException("usage: compat-override game|dlc|protocol|protocol-sent|mod-add|mod-clear|reset [value]");
        }
        return Overrides();
    }

    internal static void Reset(List<string> changed)
    {
        if (CompatOverrides.Any) changed.Add("compat-override");
        CompatOverrides.Reset();
    }

    private static Dictionary<string, object> Overrides() => new Dictionary<string, object>
    {
        ["game"] = CompatOverrides.GameVersion,
        ["dlc"] = CompatOverrides.Dlc,
        ["protocol"] = CompatOverrides.Protocol,
        ["protocolSent"] = CompatOverrides.ProtocolSent,
        ["mods"] = CompatOverrides.ExtraMods.Select(m => $"{m.Name}: {string.Join(", ", m.Targets)}").ToList(),
    };
}
