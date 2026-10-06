using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace TogetherTestHarness;

/// <summary>
/// File-based command channel: the test scripts write "&lt;seq&gt; &lt;verb&gt; [args]" to command.txt,
/// the mod executes it on the main thread and answers in reply_&lt;seq&gt;.json.
/// </summary>
public static class CommandChannel
{
    public const string CommandFile = "command.txt";

    private static float nextPoll;

    public static void Poll()
    {
        if (Time.unscaledTime < nextPoll) return;
        nextPoll = Time.unscaledTime + 0.2f;

        string path = Path.Combine(HarnessMod.Dir, CommandFile);
        if (!File.Exists(path)) return;

        string line;
        try
        {
            line = File.ReadAllText(path).Trim();
            File.Delete(path);
        }
        catch (IOException)
        {
            return;
        }

        var parts = line.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return;
        string seq = parts[0];
        string verb = parts[1];
        string args = parts.Length > 2 ? parts[2] : "";

        var reply = new Dictionary<string, object> { ["seq"] = seq, ["verb"] = verb };
        try
        {
            reply["result"] = Commands.Execute(verb, args);
            reply["ok"] = true;
        }
        catch (Exception ex)
        {
            reply["ok"] = false;
            reply["error"] = ex.ToString();
        }

        HarnessMod.Log.Msg($"[Harness] command {seq} {verb} {Redact(args)} -> ok={reply["ok"]}");
        WriteJson(Path.Combine(HarnessMod.Dir, $"reply_{seq}.json"), reply);
    }

    private static string Redact(string args) =>
        System.Text.RegularExpressions.Regex.Replace(args ?? "", @"\b(password|adminKey)=\S*", "$1=<redacted>", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static void WriteJson(string path, object value)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonConvert.SerializeObject(value, Formatting.Indented));
        if (File.Exists(path)) File.Delete(path);
        File.Move(tmp, path);
    }
}
