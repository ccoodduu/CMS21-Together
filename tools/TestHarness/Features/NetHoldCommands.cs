using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21Together.Network;
using CMS21Together.Network.Transport;
using HarmonyLib;

namespace TogetherTestHarness.Features;

// Holds incoming packets (except heartbeat and movement) and replays them in order, so a scenario can make two
// clients act on the same thing before either sees the other's change. "net-hold out" is a full stall: every incoming
// packet is held (so heartbeats are not echoed), outgoing TCP data is held and UDP dropped, until "net-hold off".
// Held or delayed packets are dropped instead of replayed once the connection is gone, as data in flight would be.
[HarmonyPatch]
public static class NetHoldCommands
{
    private static bool holding;
    private static HashSet<PacketTypes> holdOnly;
    private static bool stalling;
    private static readonly List<byte[]> heldOutgoing = new List<byte[]>();
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
            if (delayed.Count > 0 && !Connected) delayed.Clear();
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
        string mode = (args ?? "").Trim();
        if (mode == "status") return new { holding, stalling, heldIn = held.Count, heldOut = heldOutgoing.Count };
        if (mode == "on" || mode == "out")
        {
            holding = true;
            stalling = mode == "out";
            holdOnly = null;
            return stalling ? "stalling both directions" : "holding";
        }
        if (mode.StartsWith("only "))
        {
            holding = true;
            stalling = false;
            holdOnly = new HashSet<PacketTypes>(mode.Substring(5).Split(',').Select(t => (PacketTypes)System.Enum.Parse(typeof(PacketTypes), t.Trim(), true)));
            return $"holding only {string.Join(",", holdOnly)}";
        }
        holding = false;
        holdOnly = null;
        stalling = false;
        var replay = new List<(PacketTypes Id, object Data, long Sender)>(held);
        var outgoing = new List<byte[]>(heldOutgoing);
        held.Clear();
        heldOutgoing.Clear();
        if (!Connected) return $"dropped {replay.Count} incoming and {outgoing.Count} outgoing (not connected)";
        int sent = SendHeld(outgoing);
        replaying = true;
        try
        {
            foreach (var packet in replay) PacketRouter.Dispatch(packet.Id, packet.Data, packet.Sender);
        }
        finally
        {
            replaying = false;
        }
        return outgoing.Count > 0 ? $"replayed {replay.Count}, sent {sent} held outgoing" : $"replayed {replay.Count}";
    }

    private static bool Connected => Client.Instance != null && Client.Instance.IsConnected;

    private static int SendHeld(List<byte[]> outgoing)
    {
        var stream = Client.Instance.Tcp == null ? null : Traverse.Create(Client.Instance.Tcp).Field("stream").GetValue<NetworkStream>();
        if (stream == null) return 0;
        foreach (var buffer in outgoing) stream.Write(buffer, 0, buffer.Length);
        return outgoing.Count;
    }

    internal static void Reset(List<string> changed)
    {
        if (holding || held.Count > 0 || heldOutgoing.Count > 0) changed.Add($"net-hold{(stalling ? " out" : "")} (dropped {held.Count} held packets, {heldOutgoing.Count} outgoing)");
        if (delaySeconds > 0f || delayed.Count > 0) changed.Add($"net-delay {delaySeconds * 1000f:0} ms (dropped {delayed.Count} delayed packets)");
        holding = false;
        stalling = false;
        holdOnly = null;
        held.Clear();
        heldOutgoing.Clear();
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
        if (!holding || replaying) return true;
        if (!stalling && (id == PacketTypes.Heartbeat || id == PacketTypes.Movement)) return true;
        if (holdOnly != null && !holdOnly.Contains(id)) return true;
        held.Add((id, deserializedData, senderId));
        return false;
    }

    [HarmonyPatch(typeof(ClientTCP), nameof(ClientTCP.SendData))]
    [HarmonyPrefix]
    private static bool BeforeTcpSend(Packet packet)
    {
        if (!stalling) return true;
        heldOutgoing.Add(packet.ToArray());
        return false;
    }

    [HarmonyPatch(typeof(ClientUDP), nameof(ClientUDP.SendData))]
    [HarmonyPrefix]
    private static bool BeforeUdpSend() => !stalling;
}
