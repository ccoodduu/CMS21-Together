using System;
using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;

namespace CMS21_Together_Server.Data.Cars
{
	// --check-locks: the D2/D3 rules of CarLocks on a synthetic car, in-process and without clients.
	public static class CarLocksCheck
	{
		private const int Loader = 0;
		private const int OtherLoader = 1;
		private const int A = 1;
		private const int B = 2;

		private static int failures;
		private static int nextRequest = 1;

		public static int Run()
		{
			var state = new ModGameState();
			state.CarState.LoadedCars[Loader] = Car("3", "3.22", "3.22.4", "3.22.5", "3.2", "3.2.1");
			state.CarState.LoadedCars[OtherLoader] = Car("1");
			state.InventoryState.InventoryItems.Add(new ModItem { UID = 500, ID = "caliper" });
			state.InventoryState.InventoryItems.Add(new ModItem { UID = 501, ID = "piston" });
			GameDataManager.UseStateForCheck(state);
			CarLocks.Reset();
			CarLocks.Scope = LockScope.Connected;

			var cap = Grant(A, X("s:3.22.4"));
			Expect("same key is refused", Ask(B, X("s:3.22.4")), CarLockRefusal.Held, "s:3.22.4");
			Expect("X on the parent of another player's part is refused, naming that part", Ask(B, X("s:3.22")), CarLockRefusal.Held, "s:3.22.4");
			Expect("a sibling is granted (S/S on the parent)", Ask(B, X("s:3.22.5")), CarLockRefusal.None, null);
			Expect("a look-alike prefix is not an ancestor", Ask(B, X("s:3.2")), CarLockRefusal.None, null);
			Expect("an exclusive fluid lock is granted", AskWith(B, X("f:EngineCoolant.0"), S()), CarLockRefusal.None, null);
			Expect("an X fluid blocks a part that holds it shared", Ask(A, X("b:0"), S("f:EngineCoolant.0")), CarLockRefusal.Held, "f:EngineCoolant.0");
			Expect("car X is refused while another player works on the car", Ask(B, X(LockKeys.Car), kind: CarLockKind.Lift), CarLockRefusal.Held, "s:3.22.4");
			Expect("an unknown part key is invalid", Ask(B, X("s:9.9")), CarLockRefusal.Invalid, "s:9.9");
			Expect("a stale SpawnSeq is refused", Ask(B, X("s:3"), spawnSeq: 99), CarLockRefusal.Stale, null);
			CarLocks.ReleaseOwner(A, "check");
			CarLocks.ReleaseOwner(B, "check");
			Grant(A, X("s:3.22.4"));
			Expect("the same owner never conflicts with itself", Ask(A, X("s:3.22")), CarLockRefusal.None, null);
			CarLocks.ReleaseOwner(A, "check");

			var item = Grant(A, X("s:3.22.4"), items: new[] { 500L, 999L });
			Check("a known item is locked, an unknown one is left out", item != null && item.Items.SequenceEqual(new[] { 500L }));
			Expect("an item in another player's lock is refused", Ask(B, X("s:3.2.1"), items: new[] { 500L }), CarLockRefusal.Item, "i:500");
			var extend = TryGrant(A, Request(X("s:3.22.4"), extendLockId: item?.Id ?? 0, items: new[] { 501L }));
			Check("extend merges items into the same lock and moves its phase", extend.Refused == null && extend.Locks[0].Id == item?.Id && extend.Locks[0].Phase == 1 && extend.Locks[0].Items.Count == 2);
			state.InventoryState.InventoryItems.RemoveAll(i => i.UID == 500);
			Expect("an item the holder already took out of the inventory is still refused", Ask(B, X("s:3.2.1"), items: new[] { 500L }), CarLockRefusal.Item, "i:500");
			state.InventoryState.InventoryItems.Add(new ModItem { UID = 500, ID = "caliper" });
			CarLocks.ReleaseOwner(A, "check");

			var swap = TryGrant(A, Request(X(LockKeys.Car), kind: CarLockKind.Move, otherLoader: OtherLoader));
			Check("a swap grants two linked records", swap.Refused == null && swap.Locks.Count == 2 && swap.Locks[0].LinkedId == swap.Locks[1].Id && swap.Locks[1].Loader == OtherLoader);
			Expect("work on the swap partner is refused", Ask(B, X("s:1"), loader: OtherLoader), CarLockRefusal.Held, LockKeys.Car);
			CarLocks.Release(swap.Locks[0].Id, "check");
			Check("releasing one swap record releases its partner", !CarLocks.All.Any());

			var move = TryGrant(A, Request(X(LockKeys.Car), kind: CarLockKind.Move, place: 3));
			Check("a move to a free place holds that place", move.Refused == null && move.Locks[0].Place == 3);
			Expect("another car's move to the same place is refused, naming the place", TryGrant(B, Request(X(LockKeys.Car), kind: CarLockKind.Move, loader: OtherLoader, place: 3)).Refused, CarLockRefusal.Held, LockKeys.Place(3));
			Expect("another car's move to a different place is granted", TryGrant(B, Request(X(LockKeys.Car), kind: CarLockKind.Move, loader: OtherLoader, place: 4)).Refused, CarLockRefusal.None, null);
			CarLocks.ReleaseOwner(A, "check");
			CarLocks.ReleaseOwner(B, "check");

			var unmount = Grant(A, X("s:3.22.4"));
			state.CarState.LoadedCars[Loader].SubParts["3.22.4"].Unmounted = true;
			CarLocks.ReleaseCommitted(A, state.CarState.LoadedCars[Loader], Loader);
			Check("a commit that flips every X part releases the lock", unmount != null && !CarLocks.All.Any());

			state.CarState.LoadedCars[Loader].SubParts["3.22.4"].Unmounted = false;
			var group = Grant(A, X("s:3.22.4", "s:3.22.5"));
			state.CarState.LoadedCars[Loader].SubParts["3.22.5"].Unmounted = true;
			CarLocks.ReleaseCommitted(A, state.CarState.LoadedCars[Loader], Loader);
			Check("a commit that flips only part of X keeps the lock", group != null && CarLocks.All.Count() == 1);
			CarLocks.ReleaseOwner(A, "check");

			CarLocks.Scope = LockScope.Part;
			Grant(A, X("s:3.22.4"));
			Expect("with lock_scope = part, the parent is granted", Ask(B, X("s:3.22")), CarLockRefusal.None, null);
			CarLocks.Scope = LockScope.Connected;
			CarLocks.ReleaseOwner(A, "check");
			CarLocks.ReleaseOwner(B, "check");

			Check("no overlap was ever recorded", CarLocks.Counter("overlapViolations") == 0);
			Console.WriteLine($"locks check: {(failures == 0 ? "OK" : $"FAILED ({failures})")}");
			return failures == 0 ? 0 : 1;
		}

		private static CarLoaderEntry Car(params string[] subKeys)
		{
			var entry = new CarLoaderEntry { SpawnSeq = 1, HasBaseline = true };
			foreach (string key in subKeys) entry.SubParts[key] = new CarSubPartUpdatePacket { PartIndexPath = key.Split('.').Select(int.Parse).ToArray() };
			entry.BodyParts[0] = new CarBodyPartUpdatePacket { PartIndex = 0 };
			return entry;
		}

		private static List<string> X(params string[] keys) => keys.ToList();

		private static List<string> S(params string[] keys) => keys.ToList();

		private static CarLockRequestPacket Request(List<string> x, List<string> s = null, CarLockKind kind = CarLockKind.PartUnmount, int loader = Loader,
			int spawnSeq = 1, int extendLockId = 0, long[] items = null, int otherLoader = -1, int place = -1) => new CarLockRequestPacket
		{
			RequestId = nextRequest++, CarLoaderID = loader, SpawnSeq = spawnSeq, Kind = kind, X = x, S = s ?? new List<string>(),
			Items = (items ?? new long[0]).ToList(), ExtendLockId = extendLockId, OtherLoaderID = otherLoader, OtherSpawnSeq = 1, Place = place
		};

		private static (CarLocks.Refused Refused, List<CarLocks.Lock> Locks) TryGrant(int client, CarLockRequestPacket request)
		{
			var refused = CarLocks.TryGrant(client, request, 0f, out var granted);
			return (refused, granted);
		}

		private static CarLocks.Lock Grant(int client, List<string> x, List<string> s = null, long[] items = null)
		{
			var result = TryGrant(client, Request(x, s, items: items));
			if (result.Refused != null)
			{
				Check($"grant {string.Join(",", x)} to {client} ({result.Refused.Refusal} {result.Refused.Key})", false);
				return null;
			}
			return result.Locks[0];
		}

		private static CarLocks.Refused Ask(int client, List<string> x, List<string> s = null, CarLockKind kind = CarLockKind.PartUnmount, int loader = Loader, int spawnSeq = 1, long[] items = null) =>
			TryGrant(client, Request(x, s, kind, loader, spawnSeq, items: items)).Refused;

		private static CarLocks.Refused AskWith(int client, List<string> x, List<string> s) => TryGrant(client, Request(x, s)).Refused;

		private static void Expect(string what, CarLocks.Refused refused, CarLockRefusal expected, string key)
		{
			var actual = refused?.Refusal ?? CarLockRefusal.None;
			bool ok = actual == expected && (key == null || refused?.Key == key);
			Check($"{what} (got {actual}{(refused?.Key != null ? $" {refused.Key}" : "")})", ok);
		}

		private static void Check(string what, bool ok)
		{
			Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
			if (!ok) failures++;
		}
	}
}
