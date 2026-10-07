using System.Collections.Generic;
using CMS21Together.Network;
using UnityEngine;

namespace CMS21Together.Logic.Outdoor;

public static class Reseed
{
	private static readonly Stack<Random.State> saved = new Stack<Random.State>();

	public static int Steps { get; private set; }

	public static bool Active => OutdoorSession.IsShared && Client.Instance != null && Client.Instance.IsConnectionValid;

	public static bool Begin(string kind, int entryState, int loop, int carIndex)
	{
		if (!Active) return false;
		saved.Push(Random.state);
		Random.InitState(StepSeed(OutdoorSession.Seed, kind, entryState, loop, carIndex));
		Steps++;
		return true;
	}

	public static void End(bool begun)
	{
		if (begun && saved.Count > 0) Random.state = saved.Pop();
	}

	public static void Reset()
	{
		saved.Clear();
		Steps = 0;
	}

	public static int StepSeed(int seed, string kind, int entryState, int loop, int carIndex)
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
			Mix(entryState);
			Mix(loop);
			Mix(carIndex);
			return (int)hash;
		}
	}
}
