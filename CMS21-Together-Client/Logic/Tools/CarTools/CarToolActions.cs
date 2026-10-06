using System.Collections;
using System.Collections.Generic;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Details;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Network;
using MelonLoader;
using UnityEngine;

namespace CMS21Together.Logic.Tools.CarTools;

// sync-workshop-car-tools D1: the acting client runs the tool and marks the car dirty for rows 1/4 at the tool's end;
// other clients only play the tool's particles and sound, never DoWorkAnim/StartAnim (they lock controls and the car).
public static class CarToolActions
{
	private const float DetailsSettleSeconds = 1f;
	private const float EffectTailSeconds = 0.5f;
	private const string PaintSfx = "CarPaint";

	private static readonly Dictionary<ToolActionKind, int> seen = new Dictionary<ToolActionKind, int>();
	private static readonly HashSet<ToolActionKind> localBusy = new HashSet<ToolActionKind>();
	private static CMS.Managers.PaintshopManager paintshop;

	public static IReadOnlyDictionary<ToolActionKind, int> Seen => seen;

	public static void Reset()
	{
		seen.Clear();
		localBusy.Clear();
	}

	public static int LoaderOf(CarLoader carLoader)
	{
		var places = CarLoaderPlaces.Get();
		return carLoader == null || places == null ? -1 : places.GetCarLoaderId(carLoader);
	}

	public static void Started(ModToolId tool, CarLoader carLoader, ToolActionKind kind)
	{
		localBusy.Add(kind);
		Send(tool, carLoader, kind);
	}

	public static void Finished(ToolActionKind kind) => localBusy.Remove(kind);

	public static void Send(ModToolId tool, CarLoader carLoader, ToolActionKind kind)
	{
		int loader = LoaderOf(carLoader);
		if (!ToolSync.CanSend || loader < 0 || !CarPartsSync.IsReady(loader)) return;
		Log.Info($"[Tools] {tool}: {kind} on loader {loader}.");
		Client.Instance.Send(new ToolActionPacket { Tool = tool, CarLoaderID = loader, Kind = kind });
	}

	public static void MarkParts(CarLoader carLoader, params string[] partNames)
	{
		int loader = LoaderOf(carLoader);
		if (loader < 0) return;
		foreach (string name in partNames) CarPartsSync.MarkDirty(loader, carLoader.GetCarPart(name));
	}

	public static void MarkDetails(CarLoader carLoader, CarDetailSection sections)
	{
		if (carLoader == null) return;
		CarDetailsSync.MarkDirty(carLoader, sections);
		MelonCoroutines.Start(MarkDetailsAgain(carLoader, sections));
	}

	private static IEnumerator MarkDetailsAgain(CarLoader carLoader, CarDetailSection sections)
	{
		yield return new WaitForSeconds(DetailsSettleSeconds);
		if (carLoader != null) CarDetailsSync.MarkDirty(carLoader, sections);
	}

	public static void OnRemote(ToolActionPacket packet)
	{
		seen[packet.Kind] = seen.TryGetValue(packet.Kind, out int count) ? count + 1 : 1;
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(packet.CarLoaderID);
		if (carLoader == null || !carLoader.IsCarLoaded()) return;
		Log.Debug($"[Tools] {packet.Tool}: remote {packet.Kind} on loader {packet.CarLoaderID}.");
		var tools = ToolsMoveManager.Get();
		switch (packet.Kind)
		{
			case ToolActionKind.Weld:
				PlayTool(packet.Kind, tools?.WelderLogic, carLoader);
				break;
			case ToolActionKind.Wash:
				PlayTool(packet.Kind, tools?.CarWashLogic, carLoader);
				break;
			case ToolActionKind.InteriorDetailing:
				PlayTool(packet.Kind, tools?.InteriorDetailingToolkitLogic, carLoader);
				break;
			case ToolActionKind.PaintCar:
				PlayPaint(carLoader);
				break;
		}
	}

	private static void PlayTool(ToolActionKind kind, GarageTool logic, CarLoader carLoader)
	{
		if (logic == null || logic.particles == null || localBusy.Contains(kind)) return;
		logic.particles.Play();
		if (!string.IsNullOrEmpty(logic.sfx)) SoundManager.Get()?.PlaySFX(logic.sfx, CarPosition(carLoader));
		MelonCoroutines.Start(StopAfter(logic.particles, logic.effectTime));
	}

	private static void PlayPaint(CarLoader carLoader)
	{
		if (paintshop == null) paintshop = Object.FindObjectOfType<CMS.Managers.PaintshopManager>();
		if (paintshop == null || paintshop.IsPainting || paintshop.particleSystem == null) return;
		paintshop.particleSystem.Play();
		SoundManager.Get()?.PlaySFX(PaintSfx, CarPosition(carLoader));
		MelonCoroutines.Start(StopAfter(paintshop.particleSystem, paintshop.carPaintDuration));
	}

	private static IEnumerator StopAfter(ParticleSystem particles, float seconds)
	{
		yield return new WaitForSeconds(seconds + EffectTailSeconds);
		if (particles != null) particles.Stop();
	}

	private static Vector3 CarPosition(CarLoader carLoader)
	{
		var root = carLoader.GetRoot();
		return root != null ? root.transform.position : carLoader.transform.position;
	}
}
