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

    [HarmonyPatch(typeof(PacketRouter), nameof(PacketRouter.Dispatch))]
    [HarmonyPrefix]
    private static bool BeforeDispatch(PacketTypes id, object deserializedData, long senderId)
    {
        if (!holding || replaying || id == PacketTypes.Heartbeat || id == PacketTypes.Movement) return true;
        held.Add((id, deserializedData, senderId));
        return false;
    }
}
