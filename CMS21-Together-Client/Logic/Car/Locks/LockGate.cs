using System;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Network;
using UnityEngine;

namespace CMS21Together.Logic.Car.Locks;

public sealed class GatedAction
{
	public LockSet Set;
	public object Target;
	public string TargetKey;
	public Func<bool> Context;
	public Action Run;
	public Func<bool> Started;
	public Action<int> OnStarted;
	public int PrefetchedLockId;
	public int ExtendLockId;
	public int OtherLoader = -1;
	public bool EndsSlotOnFailure;
}

public sealed class GateReport
{
	public string Result;
	public int Holder = -1;
	public string ConflictKey;
	public float WaitedMs;
	public bool Ran;
	public bool Started;
	public int LockId;
}

public static class LockGate
{
	public const float WaitingHintSeconds = 0.15f;

	private sealed class PendingAction
	{
		public GatedAction Action;
		public int RequestId;
		public float SentAt;
		public bool HintShown;
	}

	private static object bypassTarget;
	private static PendingAction pending;

	public static event Action<GatedAction, GateReport> Reported;

	public static bool Active => ClientScene.IsGarageReady && Client.Instance != null && Client.Instance.IsConnectionValid && ClientData.IsInitialSyncFinished;

	public static bool IsBypassed(object target) => target != null && bypassTarget != null && Equals(Unwrap(target), Unwrap(bypassTarget));

	private static object Unwrap(object target) => target is UnhollowerBaseLib.Il2CppObjectBase il2cpp ? (object)il2cpp.Pointer : target;

	public static void Reset()
	{
		pending = null;
		bypassTarget = null;
	}

	public static bool Enter(GatedAction action)
	{
		if (IsBypassed(action.Target) || !Active || action.Set == null) return true;
		int loader = action.Set.Loader;

		if (!CarPartsSync.IsReady(loader))
		{
			Refuse(action, LockMessages.Loading, "notReady");
			return false;
		}
		if (Away.CarAwaySync.BlockIfLocked(loader, "part edit"))
		{
			ResetButton();
			Report(action, new GateReport { Result = "away", Holder = Away.CarAwaySync.LockedForMe(loader, out int awayOwner, out _) ? awayOwner : -1, ConflictKey = LockKeys.Car });
			return false;
		}
		if (CarMotion.IsMoving(loader, out int mover))
		{
			Refuse(action, LockMessages.Moving(loader, mover), "moving");
			return false;
		}
		var conflict = CarLockMirror.Conflict(action.Set);
		if (conflict != null)
		{
			CarLockMirror.Count("refusedLocally");
			Refuse(action, LockMessages.ForConflict(loader, conflict, action.TargetKey), "refusedLocally", conflict.Holder, conflict.Key);
			return false;
		}
		if (action.PrefetchedLockId != 0)
		{
			CarLockMirror.Count("prefetchUsed");
			Grant(action, action.PrefetchedLockId, 0f);
			return false;
		}
		if (pending != null)
		{
			if (SameTarget(pending.Action, action))
			{
				ResetButton();
				return false;
			}
			CarLockMirror.Cancel(pending.RequestId);
			pending = null;
		}

		ResetButton();
		var entry = new PendingAction { Action = action, SentAt = Time.realtimeSinceStartup };
		pending = entry;
		entry.RequestId = CarLockMirror.Request(action.Set, answer => OnAnswer(entry, answer), action.ExtendLockId, action.OtherLoader);
		return false;
	}

	private static bool SameTarget(GatedAction a, GatedAction b) =>
		a.Set.Kind == b.Set.Kind && a.Set.Loader == b.Set.Loader && Equals(Unwrap(a.Target), Unwrap(b.Target));

	private static void OnAnswer(PendingAction entry, LockAnswer answer)
	{
		if (pending == entry) pending = null;
		var action = entry.Action;
		switch (answer.Outcome)
		{
			case LockOutcome.Granted:
				Grant(action, answer.LockId, answer.WaitedMs);
				break;
			case LockOutcome.Denied:
				string text = answer.Refusal == CarLockRefusal.Stale || answer.Refusal == CarLockRefusal.NotReady
					? LockMessages.Loading
					: LockMessages.ForKey(action.Set.Loader, LockMessages.Name(answer.Holder), answer.ConflictKey, action.TargetKey);
				LockMessages.Refuse(text);
				Report(action, new GateReport { Result = "denied", Holder = answer.Holder, ConflictKey = answer.ConflictKey, WaitedMs = answer.WaitedMs });
				break;
			case LockOutcome.Timeout:
				LockMessages.Refuse(LockMessages.NoAnswer);
				Report(action, new GateReport { Result = "timeout", WaitedMs = answer.WaitedMs });
				break;
			default:
				Report(action, new GateReport { Result = "dropped", WaitedMs = answer.WaitedMs });
				break;
		}
	}

	private static void Grant(GatedAction action, int lockId, float waitedMs)
	{
		if (action.Context != null && !action.Context())
		{
			CarLockMirror.Count("contextLost");
			CarLockMirror.Release(lockId);
			Report(action, new GateReport { Result = "context-lost", WaitedMs = waitedMs, LockId = lockId });
			return;
		}
		bool started = false;
		bypassTarget = action.Target;
		try
		{
			action.Run();
			started = action.Started == null || action.Started();
		}
		catch (Exception e)
		{
			Log.Error($"[Locks] Re-invoked {action.Set.Kind} on loader {action.Set.Loader} failed: {e.Message}");
		}
		finally
		{
			bypassTarget = null;
		}
		if (!started)
		{
			CarLockMirror.Count("notStarted");
			CarLockMirror.Release(lockId);
			Log.Debug($"[Locks] {action.Set.Kind} on loader {action.Set.Loader} did not start; lock {lockId} released.");
		}
		else
		{
			try { action.OnStarted?.Invoke(lockId); }
			catch (Exception e) { Log.Error($"[Locks] Lock {lockId} start handler failed: {e.Message}"); }
		}
		Report(action, new GateReport { Result = "granted", WaitedMs = waitedMs, Ran = true, Started = started, LockId = lockId });
	}

	private static void Refuse(GatedAction action, string text, string result, int holder = -1, string key = null)
	{
		ResetButton();
		LockMessages.Refuse(text);
		Report(action, new GateReport { Result = result, Holder = holder, ConflictKey = key });
	}

	private static void Report(GatedAction action, GateReport report)
	{
		try { Reported?.Invoke(action, report); }
		catch (Exception e) { Log.Error($"[Locks] Gate report handler failed: {e.Message}"); }
	}

	private static void ResetButton()
	{
		try { Cursor3D.Get()?.ResetButton(false); }
		catch (Exception) { }
	}

	public static void Update()
	{
		if (pending == null || pending.HintShown) return;
		if (Time.realtimeSinceStartup - pending.SentAt < WaitingHintSeconds) return;
		pending.HintShown = true;
		LockMessages.Show(LockMessages.Waiting);
	}

	public static bool HasPending => pending != null;
}
