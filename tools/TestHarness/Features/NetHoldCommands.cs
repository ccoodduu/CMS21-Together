using System.Collections.Generic;
using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using HarmonyLib;

namespace TogetherTestHarness.Features;

// Holds incoming packets (except heartbeat and movement) and replays them in order, so a scenario can make two
// clients act on the same thing before either sees the other's change.
[HarmonyPatch]
public static class NetHoldCommands
{
    private static bool holding;
    private static bool replaying;
    private static readonly List<(PacketTypes Id, object Data, long Sender)> held = new List<(PacketTypes, object, long)>();
    private static float delaySeconds;
    private static readonly Queue<(float Due, PacketTypes Id, object Data, long Sender)> delayed = new Queue<(float, PacketTypes, object, long)>();
    private static bool pumping;

    [HarnessCommand("net-delay")]
    private static object NetDelay(string args)
    {
        delaySeconds = int.Parse((args ?? "0").Trim()) / 1000f;
        if (delaySeconds > 0f && !pumping)
        {
            pumping = true;
            MelonLoader.MelonCoroutines.Start(Pump());
        }
        return $"incoming packets delayed by {delaySeconds * 1000f:0} ms";
    }

    private static System.Collections.IEnumerator Pump()
    {
        while (delaySeconds > 0f || delayed.Count > 0)
        {
            while (delayed.Count > 0 && (delayed.Peek().Due <= UnityEngine.Time.realtimeSinceStartup || delaySeconds <= 0f))
            {
                var packet = delayed.Dequeue();
                replaying = true;
                try { PacketRouter.Dispatch(packet.Id, packet.Data, packet.Sender); }
                finally { replaying = false; }
            }
            yield return null;
        }
        pumping = false;
    }

    [HarnessCommand("net-hold")]
    private static object NetHold(string args)
    {
        if ((args ?? "").Trim() == "on")
        {
            holding = true;
            return "holding";
        }
        holding = false;
        var replay = new List<(PacketTypes Id, object Data, long Sender)>(held);
        held.Clear();
        replaying = true;
        try
        {
            foreach (var packet in replay) PacketRouter.Dispatch(packet.Id, packet.Data, packet.Sender);
        }
        finally
        {
            replaying = false;
        }
        return $"replayed {replay.Count}";
    }

    internal static void Reset(List<string> changed)
    {
        if (holding || held.Count > 0) changed.Add($"net-hold (dropped {held.Count} held packets)");
        if (delaySeconds > 0f || delayed.Count > 0) changed.Add($"net-delay {delaySeconds * 1000f:0} ms (dropped {delayed.Count} delayed packets)");
        holding = false;
        held.Clear();
        delaySeconds = 0f;
        delayed.Clear();
    }

    [HarmonyPatch(typeof(PacketRouter), nameof(PacketRouter.Dispatch))]
    [HarmonyPrefix]
    private static bool BeforeDispatch(PacketTypes id, object deserializedData, long senderId)
    {
        if (!holding && delaySeconds > 0f && !replaying && id != PacketTypes.Heartbeat)
        {
            delayed.Enqueue((UnityEngine.Time.realtimeSinceStartup + delaySeconds, id, deserializedData, senderId));
            return false;
        }
        if (!holding || replaying || id == PacketTypes.Heartbeat || id == PacketTypes.Movement) return true;
        held.Add((id, deserializedData, senderId));
        return false;
    }
}
