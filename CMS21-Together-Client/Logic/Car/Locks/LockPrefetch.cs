using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using UnityEngine;

namespace CMS21Together.Logic.Car.Locks;

public static class LockPrefetch
{
	private sealed class Prefetch
	{
		public int Loader;
		public string Key;
		public System.IntPtr Part;
		public int RequestId;
		public int LockId;
		public bool Answered;
	}

	private static Prefetch current;
	private static bool lastCounting;

	public static void Reset()
	{
		current = null;
		lastCounting = false;
	}

	public static int Take(int loader, LockSet set)
	{
		var p = current;
		if (p == null || p.Loader != loader || set.X.Count == 0 || set.X[0] != p.Key) return 0;
		current = null;
		if (p.LockId != 0) return p.LockId;
		CarLockMirror.Cancel(p.RequestId);
		return 0;
	}

	public static void Update()
	{
		var cursor = Cursor3D.Get();
		bool counting = cursor != null && cursor.canCountTime;
		bool started = counting && !lastCounting;
		lastCounting = counting;

		var game = GameScript.Get();
		var part = game?.partMouseOver;
		if (current != null && (!counting || part == null || part.Pointer != current.Part))
		{
			Drop("hold ended or moved off the part");
			return;
		}
		if (!started || current != null || part == null || !LockGate.Active) return;
		var mode = GameMode.Get()?.currentMode;
		if (mode != gameMode.PartSelect || part.IsUnmounted) return;
		if (!LockHooks.TryResolve(part, out int loader, out string key)) return;
		var set = LockSets.ForPart(loader, key, CarLockKind.PartUnmount);
		if (set == null || CarLockMirror.Conflict(set) != null || CarMotion.IsMoving(loader, out _)) return;
		var p = new Prefetch { Loader = loader, Key = set.X[0], Part = part.Pointer };
		current = p;
		CarLockMirror.Count("prefetched");
		p.RequestId = CarLockMirror.Request(set, answer =>
		{
			p.Answered = true;
			if (answer.Outcome != LockOutcome.Granted) return;
			if (current == p) p.LockId = answer.LockId;
			else CarLockMirror.Release(answer.LockId);
		});
	}

	private static void Drop(string why)
	{
		var p = current;
		current = null;
		CarLockMirror.Count("prefetchAborted");
		if (p.LockId != 0) CarLockMirror.Release(p.LockId);
		else if (!p.Answered) CarLockMirror.Cancel(p.RequestId);
		Log.Debug($"[Locks] Prefetch of {p.Key} dropped: {why}.");
	}
}
