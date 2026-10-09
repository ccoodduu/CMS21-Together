using System;
using System.Linq;
using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Digest;
using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Persistence.Sections;
using CMS21_Together_Server.Data.Presence;
using Newtonsoft.Json.Linq;

namespace CMS21_Together_Server.Data.Garage
{
	// --check-garage-look: the D1 clamp, the v1 -> v2 migration of the garage section, the claim and its release on
	// leave and on leaving the garage, and the look in the garage digest, in-process and without clients.
	public static class GarageLookCheck
	{
		private const int A = 1;
		private const int B = 2;

		private static int failures;

		public static int Run()
		{
			GameDataManager.UseStateForCheck(new ModGameState());
			GarageLookService.Initialize();
			GarageLookService.ResetRuntime();

			var clamped = new ModGarageLook { MaterialIndexes = new[] { -5, 0, 70, 3 }, TexturePack = "", SectionCount = 200 }.Clamped();
			Check("indexes are clamped to -1..63", clamped.MaterialIndexes.SequenceEqual(new[] { -1, 0, 63, 3 }));
			Check("an empty pack id is the default pack", clamped.TexturePack == null);
			Check("the section count is clamped to 64", clamped.SectionCount == ModGarageLook.MaxSections);
			Check("at most 64 sections are kept", new ModGarageLook { MaterialIndexes = new int[80] }.Clamped().MaterialIndexes.Length == ModGarageLook.MaxSections);
			Check("a missing section is the default material", new ModGarageLook { MaterialIndexes = new[] { 2 } }.MaterialAt(5) == ModGarageLook.DefaultMaterial);
			Check("trailing defaults do not change the look", new ModGarageLook { MaterialIndexes = new[] { 2, -1, -1 } }.SameLook(new ModGarageLook { MaterialIndexes = new[] { 2 } }));

			var v1 = new JObject { ["GarageUpgradeLevels"] = new JObject { ["lifter"] = new JArray(true, false) }, ["PlayerUpgradeLevels"] = new JObject() };
			var section = new GarageSection();
			section.Load(section.Migrate(v1, 1));
			var migrated = GameDataManager.CurrentState.GarageState;
			Check("v1 -> v2 keeps the upgrades", migrated.GarageUpgradeLevels.TryGetValue("lifter", out var lifter) && lifter.SequenceEqual(new[] { true, false }));
			Check("v1 -> v2 adds an empty look", migrated.Look != null && migrated.Look.MaterialIndexes.Length == 0 && migrated.Look.TexturePack == null && migrated.Look.SectionCount == 0);
			Check("the section is v2", section.Version == 2);

			Check("A claims the free window", GarageLookService.Claim(A));
			Check("A may claim again", GarageLookService.Claim(A));
			Check("B is refused while A holds it", !GarageLookService.Claim(B) && GarageLookService.Holder == A);
			Check("an update from B is refused", !GarageLookService.Store(B, new ModGarageLook { MaterialIndexes = new[] { 1 }, SectionCount = 41 }));
			Check("A stores a clamped look", GarageLookService.Store(A, new ModGarageLook { MaterialIndexes = new[] { 99, -1, 4 }, TexturePack = "pack", SectionCount = 41 })
				&& GarageLookService.Look.MaterialIndexes.SequenceEqual(new[] { 63, -1, 4 }) && GarageLookService.Look.TexturePack == "pack" && GarageLookService.Look.SectionCount == 41);
			GarageLookService.Release(B, "released");
			Check("B cannot release A's claim", GarageLookService.Holder == A);
			GarageLookService.Release(A, "released");
			Check("A's release frees the window", GarageLookService.Holder == GarageLookService.NoHolder);

			GarageLookService.Claim(B);
			PresenceEvents.RaiseSceneChanged(B, GameScene.Garage, GameScene.Junkyard);
			Check("leaving the garage releases the claim", GarageLookService.Holder == GarageLookService.NoHolder);
			GarageLookService.Claim(B);
			PresenceEvents.RaiseSceneChanged(B, GameScene.Menu, GameScene.Garage);
			Check("entering the garage keeps the claim", GarageLookService.Holder == B);
			PresenceEvents.RaiseLeft(A);
			Check("another player's disconnect keeps the claim", GarageLookService.Holder == B);
			PresenceEvents.RaiseLeft(B);
			Check("a disconnect releases the claim", GarageLookService.Holder == GarageLookService.NoHolder);
			Check("the update after the release is refused", !GarageLookService.Store(B, new ModGarageLook()));

			var saved = section.Save();
			section.Load(saved);
			Check("the look survives a save and load", GameDataManager.CurrentState.GarageState.Look.SameLook(new ModGarageLook { MaterialIndexes = new[] { 63, -1, 4 }, TexturePack = "pack" })
				&& GameDataManager.CurrentState.GarageState.Look.SectionCount == 41);

			var upgrades = new System.Collections.Generic.Dictionary<string, bool[]>();
			ulong Hash(ModGarageLook look) => DigestMappers.Garage(upgrades, upgrades, 0, look).Hash();
			Check("a null look hashes like the empty look", Hash(null) == Hash(new ModGarageLook()));
			Check("an all-default look hashes like the empty look", Hash(new ModGarageLook { MaterialIndexes = new[] { -1, -1 }, SectionCount = 41 }) == Hash(new ModGarageLook()));
			Check("a changed section changes the hash", Hash(new ModGarageLook { MaterialIndexes = new[] { -1, 3 } }) != Hash(new ModGarageLook { MaterialIndexes = new[] { -1, 2 } }));
			Check("a texture pack changes the hash", Hash(new ModGarageLook { TexturePack = "pack" }) != Hash(new ModGarageLook()));

			Console.WriteLine(failures == 0 ? "garage look check: all passed" : $"garage look check: {failures} failed");
			return failures == 0 ? 0 : 1;
		}

		private static void Check(string what, bool ok)
		{
			Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}");
			if (!ok) failures++;
		}
	}
}
