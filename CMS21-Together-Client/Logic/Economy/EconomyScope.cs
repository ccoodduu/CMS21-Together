using System;
using System.Collections.Generic;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using UnityEngine;

namespace CMS21Together.Logic.Economy;

public enum EconomyMode
{
	Fee,
	Covered,
	Suppressed
}

[Flags]
public enum EconomyKind
{
	Money = 1,
	Scraps = 2,
	Exp = 4,
	All = Money | Scraps | Exp
}

public sealed class EconomyScopeEntry
{
	public string Name;
	public EconomyReason Reason;
	public EconomyMode Mode;
	public EconomyKind Claims;
	public int Arg;
	public int Arg2;
	public long ItemUid;
	public int Loader = -1;
	public int Before;
	public int Money;
	public int Scraps;
	public int Exp;
	public Action<int> CaptureMoney;
	public Action<int> CaptureExp;
	internal int Frame;

	public bool Claim(EconomyKind kind) => (Claims & kind) != 0;
}

// economy-audit D1: the game method that is running when a mutator is called. Feature hooks push an entry in a
// prefix and pop it in the postfix; every entry lives for one frame at most, so a call that threw cannot leave one open.
public static class EconomyScope
{
	private static readonly List<EconomyScopeEntry> stack = new List<EconomyScopeEntry>();

	public static EconomyScopeEntry Push(EconomyReason reason, EconomyMode mode, EconomyKind claims = EconomyKind.Money, int arg = 0, long itemUid = 0, int loader = -1, string name = null)
	{
		DropStale();
		var entry = new EconomyScopeEntry
		{
			Name = name ?? reason.ToString(), Reason = reason, Mode = mode, Claims = claims, Arg = arg, ItemUid = itemUid, Loader = loader,
			Frame = Time.frameCount,
		};
		stack.Add(entry);
		return entry;
	}

	public static EconomyScopeEntry Covered(string name, EconomyKind claims) => Push(EconomyReason.Work, EconomyMode.Covered, claims, name: name);

	public static void Pop(EconomyScopeEntry entry)
	{
		if (entry == null) return;
		int index = stack.LastIndexOf(entry);
		if (index >= 0) stack.RemoveRange(index, stack.Count - index);
	}

	public static EconomyScopeEntry Find(EconomyKind kind)
	{
		DropStale();
		for (int i = stack.Count - 1; i >= 0; i--)
			if (stack[i].Claim(kind)) return stack[i];
		var job = Jobs.JobEndContext.Scope;
		return job != null && job.Claim(kind) ? job : null;
	}

	public static void Reset() => stack.Clear();

	private static void DropStale()
	{
		int frame = Time.frameCount;
		for (int i = stack.Count - 1; i >= 0; i--)
		{
			if (stack[i].Frame == frame) continue;
			Log.Warn($"[Economy] Scope {stack[i].Name} was left open (the hooked call did not finish); dropped.");
			stack.RemoveAt(i);
		}
	}
}
