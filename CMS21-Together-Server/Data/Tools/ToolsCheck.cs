using System;
using System.Collections.Generic;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;

namespace CMS21_Together_Server.Data.Tools
{
	// --check-tools: the rules for an engine built on the stand (ToolSlotUpdatePacket.Created), in-process and without
	// clients.
	public static class ToolsCheck
	{
		private const int A = 1;
		private const int B = 2;

		private static int failures;

		public static int Run()
		{
			GameDataManager.UseStateForCheck(new ModGameState());
			ToolsStore.ResetCounters();

			Check("a built engine on the empty stand is accepted", Put(A, 900, 0, created: true) == null && Holds(900));
			Check("the built engine is counted", ToolsStore.CreatedEngines == 1);
			string old = Put(B, 901, 0, created: true);
			Check($"a built engine that expects the empty stand while it holds one is refused ({old})", old != null && Holds(900));
			Check("a refused build is not counted", ToolsStore.CreatedEngines == 1);
			string stale = Put(B, 902, 899, created: true);
			Check($"a built engine with an old expected UID is refused ({stale})", stale != null && Holds(900));

			Check("the stand is emptied", Clear(A, 900) == null && Holds(0));
			string first = Put(A, 910, 0, created: true);
			string second = Put(B, 911, 0, created: true);
			Check($"two builds on the empty stand give one engine and one refusal (A {first ?? "accepted"}, B {second ?? "accepted"})",
				first == null && second != null && Holds(910));
			Check("only the stored build is counted", ToolsStore.CreatedEngines == 2);
			Check("an ordinary put is not counted as built", Clear(A, 910) == null && Put(A, 920, 0, created: false) == null && ToolsStore.CreatedEngines == 2);

			Console.WriteLine($"tools check: {(failures == 0 ? "OK" : $"FAILED ({failures})")}");
			return failures == 0 ? 0 : 1;
		}

		private static string Put(int client, long uid, long expected, bool created)
		{
			var group = new ModGroupItem { ID = "engine_v8_stary", UID = uid, ItemList = new List<ModItem> { new ModItem { ID = "engine_v8_stary", UID = uid + 100000 } } };
			var state = new ToolSlotState { Tool = ModToolId.EngineStand1, Group = group };
			return ToolsStore.TryStore(client, new ToolSlotUpdatePacket { State = state, ExpectedUid = expected, Created = created });
		}

		private static string Clear(int client, long expected) =>
			ToolsStore.TryStore(client, new ToolSlotUpdatePacket { State = ToolSlotState.Empty(ModToolId.EngineStand1), ExpectedUid = expected });

		private static bool Holds(long uid) => ToolsStore.Slot(ModToolId.EngineStand1).Uid == uid;

		private static void Check(string what, bool ok)
		{
			Console.WriteLine($"  {(ok ? "ok  " : "FAIL")} {what}");
			if (!ok) failures++;
		}
	}
}
