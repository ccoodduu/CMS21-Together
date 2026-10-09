using System.Collections.Generic;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Away;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Logic.Player;
using CMS21Together.UI;
using UnityEngine;

namespace CMS21Together.Logic.Car.Locks;

public static class LockMessages
{
	private const float RepeatSeconds = 2f;

	private static readonly Dictionary<string, float> shownAt = new Dictionary<string, float>();

	public static string Last { get; private set; }

	public static string Name(int playerId) =>
		PresenceManager.Roster.TryGetValue(playerId, out var player) ? player.Record.Username : $"Player {playerId}";

	public static string ForConflict(int loader, LockConflict conflict, string targetKey)
	{
		string name = Name(conflict.Holder);
		if (conflict.Refusal == CarLockRefusal.Away && CarAwaySync.LockedForMe(loader, out _, out var kind))
			return $"{name} has this car {CarAwaySync.Activity(kind)}.";
		return ForKey(loader, name, conflict.Key, targetKey, conflict.Kind);
	}

	public static string ForKey(int loader, string name, string key, string targetKey, CarLockKind? holderKind = null)
	{
		if (holderKind == CarLockKind.Tune || key == LockKeys.Tune) return Tuning(name);
		if (holderKind == CarLockKind.BonusPart || LockKeys.IsBonus(key)) return $"{name} is fitting a bonus part here.";
		if (targetKey == LockKeys.Car) return $"{name} is working on this car.";
		if (key == null || key == targetKey) return $"{name} is working on this part.";
		if (key == LockKeys.Car) return $"{name} is working on this car.";
		if (key == LockKeys.Engine) return $"{name} is working on the engine.";
		if (key.StartsWith("i:")) return $"{name} is mounting this part.";
		if (LockKeys.IsFluid(key)) return $"{name} is working on the {FluidName(key)} system.";
		string part = PartName(loader, key);
		return part == null ? $"{name} is working on this part." : $"{name} is working on the {part}.";
	}

	public static string Moving(int loader, int holder) => holder >= 0 ? $"{Name(holder)} is moving this car." : "This car is moving.";

	public static string Busy(int holder) => $"{Name(holder)} is working on this car.";

	public static string Tuning(string name) => $"{name} is tuning this car.";

	public static string ItemTag(int holder) => $"{Name(holder)} is mounting this";

	public static string OtherCar(int holder) => $"{Name(holder)} is working on the car there.";

	public const string Waiting = "Waiting for the server…";
	public const string NoAnswer = "The server did not answer. Try again.";
	public const string Loading = "This car is still loading for multiplayer.";
	public const string SlotChanged = "This slot just changed.";

	private static string FluidName(string key)
	{
		string type = key.Substring(2, key.IndexOf('.') - 2);
		switch (type)
		{
			case "EngineOil": return "engine oil";
			case "EngineCoolant": return "coolant";
			case "Brake": return "brake fluid";
			case "PowerSteering": return "power steering";
			case "WindscreenWash": return "washer fluid";
			default: return type;
		}
	}

	private static string PartName(int loader, string key)
	{
		var registry = LockSets.Relations(loader)?.Registry;
		if (registry == null) return null;
		string id = LockKeys.IsSub(key) ? registry.Sub(key)?.id : registry.Body(key)?.name;
		if (string.IsNullOrEmpty(id)) return null;
		try
		{
			var inventory = GameInventory.Instance;
			string localized = inventory == null ? null : LockKeys.IsBody(key) ? inventory.GetBodyLocalizedName(id) : inventory.GetLocalizedName(id);
			return string.IsNullOrEmpty(localized) ? id : localized;
		}
		catch (System.Exception)
		{
			return id;
		}
	}

	public static void Refuse(string text, bool sound = true)
	{
		Last = text;
		if (sound) SoundManager.Get()?.PlaySFX("Error");
		Show(text);
	}

	public static void Show(string text)
	{
		float now = Time.realtimeSinceStartup;
		if (shownAt.TryGetValue(text, out float at) && now - at < RepeatSeconds) return;
		shownAt[text] = now;
		ModNotify.ShowToast(text);
	}
}
