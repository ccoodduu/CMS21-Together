using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CMS21Together.Network;
using UnhollowerBaseLib;
using Random = UnityEngine.Random;

namespace CMS21Together.Logic.Outdoor;

public static class Reseed
{
	// UnityEngine.Random.State is unhollowed as a 1-byte struct without its four seed words, so Random.state would copy
	// 16 bytes into it; the injected icalls are called with a struct of the real size instead.
	[StructLayout(LayoutKind.Sequential)]
	public struct RandomState
	{
		public int S0, S1, S2, S3;

		public override string ToString() => $"{S0:x8}{S1:x8}{S2:x8}{S3:x8}";
	}

	private delegate void GetStateIcall(out RandomState state);
	private delegate void SetStateIcall(ref RandomState state);

	private static GetStateIcall getState;
	private static SetStateIcall setState;
	private static readonly Stack<RandomState> saved = new Stack<RandomState>();
	private static readonly Dictionary<IntPtr, RandomState> streams = new Dictionary<IntPtr, RandomState>();

	public static int Steps { get; private set; }

	public static bool Active => OutdoorSession.IsShared && Client.Instance != null && Client.Instance.IsConnectionValid;

	public static RandomState Current
	{
		get
		{
			getState ??= IL2CPP.ResolveICall<GetStateIcall>("UnityEngine.Random::get_state_Injected");
			getState(out var state);
			return state;
		}
		set
		{
			setState ??= IL2CPP.ResolveICall<SetStateIcall>("UnityEngine.Random::set_state_Injected");
			setState(ref value);
		}
	}

	public static bool Begin(IntPtr iterator, string kind, int carIndex)
	{
		if (!Active) return false;
		saved.Push(Current);
		if (streams.TryGetValue(iterator, out var stream)) Current = stream;
		else Random.InitState(StreamSeed(OutdoorSession.Seed, kind, carIndex));
		Steps++;
		return true;
	}

	public static void End(bool begun, IntPtr iterator, bool finished)
	{
		if (!begun) return;
		if (finished) streams.Remove(iterator);
		else streams[iterator] = Current;
		if (saved.Count > 0) Current = saved.Pop();
	}

	public static T WithSeed<T>(int seed, Func<T> body)
	{
		var outside = Current;
		try
		{
			Random.InitState(seed);
			return body();
		}
		finally
		{
			Current = outside;
		}
	}

	public static void Reset()
	{
		saved.Clear();
		streams.Clear();
		Steps = 0;
	}

	public static int StreamSeed(int seed, string kind, int carIndex)
	{
		unchecked
		{
			uint hash = 2166136261;
			void Mix(int value)
			{
				for (int i = 0; i < 4; i++)
				{
					hash ^= (byte)(value >> (8 * i));
					hash *= 16777619;
				}
			}
			Mix(seed);
			foreach (char c in kind) Mix(c);
			Mix(carIndex);
			return (int)hash;
		}
	}
}
