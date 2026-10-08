using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace TogetherTestHarness.Features;

// stack-sample on|off|report: while the main thread has not finished a frame for 300 ms, a background thread
// suspends it every 20 ms, copies its stack and reads back every value that points into GameAssembly.dll's code
// (stack scanning: return addresses plus some noise). report counts, per GameAssembly RVA, the samples it was on;
// the RVAs map to methods with the IL2CPP dump. Finds the game method behind a frozen frame.
public static class StackSampler
{
    private const int StallMs = 300;
    private const int StackBytes = 512 * 1024;
    private const uint ThreadAccess = 0x0002 | 0x0008 | 0x0010 | 0x0040;
    private const uint ContextFull = 0x0010000B;

    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] private static extern IntPtr OpenThread(uint access, bool inherit, uint id);
    [DllImport("kernel32.dll")] private static extern uint SuspendThread(IntPtr thread);
    [DllImport("kernel32.dll")] private static extern uint ResumeThread(IntPtr thread);
    [DllImport("kernel32.dll")] private static extern bool GetThreadContext(IntPtr thread, IntPtr context);
    [DllImport("kernel32.dll")] private static extern void GetCurrentThreadStackLimits(out IntPtr low, out IntPtr high);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);

    private static long lastBeat;
    private static uint mainThreadId;
    private static long stackHigh;
    private static volatile bool armed;
    private static Thread worker;
    private static long moduleBase, moduleEnd, unityBase, unityEnd;
    private static readonly byte[] copy = new byte[StackBytes];
    private static readonly Dictionary<long, int> counts = new Dictionary<long, int>();
    private static readonly Dictionary<string, int> ripModules = new Dictionary<string, int>();
    private static readonly Dictionary<long, int> ripGame = new Dictionary<long, int>();
    private static int samples;
    private static int stalls;
    private static readonly object gate = new object();

    internal static void Beat()
    {
        if (mainThreadId == 0)
        {
            mainThreadId = GetCurrentThreadId();
            try { GetCurrentThreadStackLimits(out _, out var high); stackHigh = high.ToInt64(); } catch (Exception) { stackHigh = 0; }
        }
        Interlocked.Exchange(ref lastBeat, Stopwatch.GetTimestamp());
    }

    [HarnessCommand("stack-sample")]
    private static object Command(string args)
    {
        switch ((args ?? "").Trim())
        {
            case "on":
                Resolve();
                lock (gate) { counts.Clear(); ripModules.Clear(); ripGame.Clear(); samples = 0; stalls = 0; }
                armed = true;
                if (worker == null)
                {
                    worker = new Thread(Run) { IsBackground = true, Name = "harness-stack-sampler" };
                    worker.Start();
                }
                break;
            case "off": armed = false; break;
            case "report": break;
            default: throw new ArgumentException("usage: stack-sample on|off|report");
        }
        lock (gate)
        {
            return new Dictionary<string, object>
            {
                ["armed"] = armed,
                ["samples"] = samples,
                ["stalls"] = stalls,
                ["gameAssemblyBase"] = $"0x{moduleBase:X}",
                ["rip"] = ripModules.ToDictionary(p => p.Key, p => p.Value),
                ["ripGame"] = ripGame.OrderByDescending(p => p.Value).Take(40).Select(p => new { rva = p.Key, samples = p.Value }).ToList(),
                ["stack"] = counts.OrderByDescending(p => p.Value).Take(200).Select(p => new { rva = p.Key, samples = p.Value }).ToList(),
            };
        }
    }

    private static void Resolve()
    {
        (moduleBase, moduleEnd) = Range("GameAssembly.dll");
        (unityBase, unityEnd) = Range("UnityPlayer.dll");
    }

    private static (long, long) Range(string module)
    {
        var handle = GetModuleHandle(module);
        if (handle == IntPtr.Zero) return (0, 0);
        int peOffset = Marshal.ReadInt32(handle, 0x3C);
        int sizeOfImage = Marshal.ReadInt32(handle, peOffset + 24 + 56);
        return (handle.ToInt64(), handle.ToInt64() + sizeOfImage);
    }

    private static void Run()
    {
        IntPtr context = Marshal.AllocHGlobal(1232 + 16);
        IntPtr aligned = new IntPtr((context.ToInt64() + 15) & ~15L);
        IntPtr thread = IntPtr.Zero;
        bool inStall = false;
        while (true)
        {
            Thread.Sleep(20);
            if (!armed || mainThreadId == 0 || stackHigh == 0) continue;
            long since = (Stopwatch.GetTimestamp() - Interlocked.Read(ref lastBeat)) * 1000 / Stopwatch.Frequency;
            if (since < StallMs) { inStall = false; continue; }
            if (thread == IntPtr.Zero) thread = OpenThread(ThreadAccess, false, mainThreadId);
            if (thread == IntPtr.Zero) continue;
            if (!inStall) { inStall = true; lock (gate) stalls++; }

            int length = 0;
            long rip = 0;
            if (SuspendThread(thread) == uint.MaxValue) continue;
            try
            {
                Marshal.WriteInt32(aligned, 0x30, unchecked((int)ContextFull));
                if (GetThreadContext(thread, aligned))
                {
                    rip = Marshal.ReadInt64(aligned, 0xF8);
                    long rsp = Marshal.ReadInt64(aligned, 0x98);
                    length = (int)Math.Max(0, Math.Min(StackBytes, stackHigh - rsp));
                    if (length > 0) Marshal.Copy(new IntPtr(rsp), copy, 0, length);
                }
            }
            finally
            {
                ResumeThread(thread);
            }
            if (rip == 0) continue;

            lock (gate)
            {
                samples++;
                string where = rip >= moduleBase && rip < moduleEnd ? "GameAssembly" : rip >= unityBase && rip < unityEnd ? "UnityPlayer" : "other";
                ripModules[where] = ripModules.TryGetValue(where, out int r) ? r + 1 : 1;
                if (where == "GameAssembly") ripGame[rip - moduleBase] = ripGame.TryGetValue(rip - moduleBase, out int g) ? g + 1 : 1;
                var seen = new HashSet<long>();
                for (int i = 0; i + 8 <= length; i += 8)
                {
                    long value = BitConverter.ToInt64(copy, i);
                    if (value < moduleBase || value >= moduleEnd) continue;
                    if (seen.Add(value - moduleBase)) counts[value - moduleBase] = counts.TryGetValue(value - moduleBase, out int c) ? c + 1 : 1;
                }
            }
        }
    }
}
