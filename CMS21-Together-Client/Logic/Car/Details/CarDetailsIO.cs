using System;
using System.Collections.Generic;
using System.Linq;
using CMS.PartModules;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Parts;
using UnityEngine;

namespace CMS21Together.Logic.Car.Details;

// Reads car details from a CarLoader and applies remote ones with side-effect-free setters
// (docs/spikes/car-details.md "Side-effect-free apply"; the order follows sync-car-details D10).
public static class CarDetailsIO
{
	public const CarDetailSection Polled = CarDetailSection.Fluids | CarDetailSection.Wheels | CarDetailSection.Alignment | CarDetailSection.Info;
	public const CarDetailSection All = Polled | CarDetailSection.Tuning | CarDetailSection.Paint | CarDetailSection.BodyCosmetics | CarDetailSection.Plates | CarDetailSection.Dyno;

	public static ModCarDetails Read(CarLoader carLoader, CarDetailSection sections)
	{
		var details = new ModCarDetails();
		if (sections.HasFlag(CarDetailSection.Fluids)) details.Fluids = ReadFluids(carLoader);
		if (sections.HasFlag(CarDetailSection.Wheels)) details.Wheels = ReadWheels(carLoader);
		if (sections.HasFlag(CarDetailSection.Alignment)) details.Alignment = ReadAlignment(carLoader);
		if (sections.HasFlag(CarDetailSection.Info))
		{
			var info = carLoader.CarInfoData;
			details.Info = new ModCarInfo { Mileage = info.Mileage, BuyPrice = info.BuyPrice, CarFrom = (ModCarFrom)(int)info.CarFrom };
		}
		if (sections.HasFlag(CarDetailSection.Tuning)) details.Tuning = ReadTuning(carLoader);
		if (sections.HasFlag(CarDetailSection.Paint))
			details.Paint = new ModCarPaint { Color = ToMod(carLoader.color), FactoryColor = ToMod(carLoader.factoryColor), FactoryPaintType = (ModPaintType)(int)carLoader.factoryPaintType, IsCustom = carLoader.IsCustomPaintType, PaintData = ToMod(carLoader.GetPaintData()) };
		if (sections.HasFlag(CarDetailSection.BodyCosmetics)) details.BodyCosmetics = ReadCosmetics(carLoader);
		if (sections.HasFlag(CarDetailSection.Dyno) && !Away.DynoSync.IsOpenOn(carLoader))
			details.Dyno = new ModDynoResult { Engine = ToMod(carLoader.EngineData), MeasuredDragIndex = carLoader.MeasuredDragIndex };
		if (sections.HasFlag(CarDetailSection.Plates))
		{
			var plates = carLoader.LicensePlatesData;
			details.Plates = new ModLPData
			{
				LicensePlateNumberFront = plates.LicensePlateNumberFront, LicensePlateNumberRear = plates.LicensePlateNumberRear,
				FactoryLicensePlateNumber = plates.FactoryLicensePlateNumber, LicensePlateFrontTex = plates.LicensePlateFrontTex, LicensePlateRearTex = plates.LicensePlateRearTex,
			};
		}
		return details;
	}

	private static List<ModFluidLevel> ReadFluids(CarLoader carLoader)
	{
		var fluids = carLoader.FluidsData;
		var result = new List<ModFluidLevel>();
		if (fluids.Oil != null) result.Add(new ModFluidLevel { Type = ModCarFluidType.EngineOil, Id = 0, Level = fluids.Oil.Level, Condition = fluids.Oil.Condition });
		AddFluids(result, fluids.Brake, ModCarFluidType.Brake);
		AddFluids(result, fluids.EngineCoolant, ModCarFluidType.EngineCoolant);
		AddFluids(result, fluids.PowerSteering, ModCarFluidType.PowerSteering);
		AddFluids(result, fluids.WindscreenWash, ModCarFluidType.WindscreenWash);
		return result;
	}

	private static void AddFluids(List<ModFluidLevel> result, Il2CppSystem.Collections.Generic.List<FluidData> list, ModCarFluidType type)
	{
		for (int i = 0; list != null && i < list.Count; i++)
			result.Add(new ModFluidLevel { Type = type, Id = i, Level = list[i].Level, Condition = list[i].Condition });
	}

	private static ModCarWheel[] ReadWheels(CarLoader carLoader)
	{
		var wheels = carLoader.WheelsData?.Wheels;
		if (wheels == null) return null;
		var result = new ModCarWheel[wheels.Length];
		for (int i = 0; i < wheels.Length; i++)
		{
			var wheel = wheels[i];
			result[i] = new ModCarWheel { Width = (int)wheel.Width, RimSize = (int)wheel.Size, TireSize = (int)wheel.Profile, ET = wheel.ET, Tire = wheel.Tire, Rim = wheel.Rim };
		}
		return result;
	}

	private static ModAlignment ReadAlignment(CarLoader carLoader)
	{
		var wheels = carLoader.WheelsAlignment;
		var left = carLoader.HeadlampLeftAlignment;
		var right = carLoader.HeadlampRightAlignment;
		return new ModAlignment
		{
			FL = wheels.FL, FR = wheels.FR, RL = wheels.RL, RR = wheels.RR,
			LampLH = left.Horizontal, LampLV = left.Vertical, LampRH = right.Horizontal, LampRV = right.Vertical,
		};
	}

	private static ModCarTuning ReadTuning(CarLoader carLoader)
	{
		var tuning = new ModCarTuning();
		var gearbox = carLoader.GetRoot()?.GetComponentInChildren<GearboxHandle>();
		if (gearbox != null)
			tuning.Gearbox = new ModGearboxData { GearRatio = gearbox.gearRatio == null ? null : (float[])gearbox.gearRatio, FinalDriveRatio = gearbox.finalDriveRatio };
		foreach (var pair in Modules(carLoader))
		{
			var module = pair.Value;
			var values = module.GetValues();
			var ecu = module.TryCast<EcuModule>();
			tuning.Modules.Add(new ModPartTuning
			{
				PartKey = pair.Key,
				Data = new ModTuningData { IsTuned = module.IsTuned(), Values = values == null ? null : (short[])values, TuningValue = module.data.TuningValue },
				EcuStage = ecu == null ? -1 : ecu.Stage,
			});
		}
		return tuning;
	}

	private static Dictionary<string, PartModule> Modules(CarLoader carLoader)
	{
		var result = new Dictionary<string, PartModule>();
		int loader = CarLoaderPlaces.Get()?.GetCarLoaderId(carLoader) ?? -1;
		var registry = loader < 0 ? null : CarPartsSync.Get(loader).Registry;
		var root = carLoader.GetRoot();
		if (registry == null || root == null) return result;
		foreach (var module in root.GetComponentsInChildren<PartModule>(true))
			if (registry.TryGetSubPath(module.PartScript, out int[] path))
				result[PartKeys.Sub(path)] = module;
		return result;
	}

	private static List<ModBodyCosmetics> ReadCosmetics(CarLoader carLoader)
	{
		var result = new List<ModBodyCosmetics>();
		var parts = carLoader.carParts;
		for (int i = 0; parts != null && i < parts.Count; i++)
		{
			var part = parts[i];
			result.Add(new ModBodyCosmetics
			{
				PartIndex = i, Color = ToMod(part.Color), PaintType = (ModPaintType)(int)part.PaintType, PaintData = ToMod(part.PaintData), Livery = part.Livery, LiveryStrength = part.LiveryStrength,
				IsTinted = part.IsTinted, TintColor = ToMod(part.TintColor), Dust = part.Dust, WashFactor = part.WashFactor,
			});
		}
		return result;
	}

	public static void Apply(CarLoader carLoader, ModCarDetails details, int wheelMask = DetailsMerge.AllWheels, AlignmentFields alignmentMask = DetailsMerge.AllAlignment)
	{
		Try("wheels", () => ApplyWheels(carLoader, details.Wheels, wheelMask));
		Try("tuning", () => ApplyTuning(carLoader, details.Tuning));
		Try("fluids", () => ApplyFluids(carLoader, details.Fluids));
		Try("alignment", () => ApplyAlignment(carLoader, details.Alignment, alignmentMask));
		Try("paint", () => ApplyPaint(carLoader, details.Paint));
		Try("cosmetics", () => ApplyCosmetics(carLoader, details.BodyCosmetics));
		Try("plates", () => ApplyPlates(carLoader, details.Plates));
		Try("info", () => ApplyInfo(carLoader, details.Info));
		Try("dyno", () => ApplyDyno(carLoader, details.Dyno));
	}

	private static void Try(string what, Action apply)
	{
		try { apply(); }
		catch (Exception e) { Log.Error($"[CarDetails] Applying {what} failed: {e}"); }
	}

	private static void ApplyFluids(CarLoader carLoader, List<ModFluidLevel> fluids)
	{
		if (fluids == null) return;
		var data = carLoader.FluidsData;
		foreach (var fluid in fluids)
			data.SetLevelAndCondition(fluid.Level, fluid.Condition, (CarFluidType)(int)fluid.Type, fluid.Id);
	}

	private static void ApplyWheels(CarLoader carLoader, ModCarWheel[] wheels, int wheelMask)
	{
		if (wheels == null) return;
		var local = carLoader.WheelsData?.Wheels;
		bool front = false, rear = false;
		for (int i = 0; local != null && i < wheels.Length && i < local.Length; i++)
		{
			var wheel = wheels[i];
			if (wheel == null || !DetailsMerge.HasWheel(wheelMask, i)) continue;
			if ((int)local[i].Width == wheel.Width && (int)local[i].Size == wheel.RimSize && (int)local[i].Profile == wheel.TireSize && local[i].ET == wheel.ET) continue;
			carLoader.SetET((WheelType)i, wheel.ET);
			carLoader.SetWheelSize(wheel.Width, wheel.RimSize, wheel.TireSize, (WheelType)i);
			if (i < 2) front = true; else rear = true;
		}
		if (!front && !rear) return;
		var wheelParts = WheelPartIds(carLoader);
		if (front) carLoader.UpdateWheels(true);
		if (rear) carLoader.UpdateWheels(false);
		RestoreWheelPartIds(wheelParts);
	}

	// UpdateWheels gives every rim and tire on the axle the ids in WheelsData, which the game sets only when the car loads.
	private static List<(PartScript Script, string Id)> WheelPartIds(CarLoader carLoader)
	{
		var root = carLoader.root != null ? carLoader.root.transform : carLoader.transform;
		return root.GetComponentsInChildren<PartScript>(true)
			.Where(PartApplier.IsWheelPart)
			.Select(script => (script, PartApplier.EffectiveId(script)))
			.ToList();
	}

	private static void RestoreWheelPartIds(List<(PartScript Script, string Id)> wheelParts)
	{
		foreach (var (script, id) in wheelParts)
		{
			if (script == null || PartApplier.EffectiveId(script) == id) continue;
			script.TunePart(id);
		}
	}

	private static void ApplyAlignment(CarLoader carLoader, ModAlignment alignment, AlignmentFields mask)
	{
		if (alignment == null) return;
		var target = ReadAlignment(carLoader);
		for (int field = 0; field < CarDetailEntries.AlignmentFieldNames.Length; field++)
			if (DetailsMerge.HasAlignment(mask, field)) CarDetailEntries.SetAlignmentValue(target, field, CarDetailEntries.AlignmentValue(alignment, field));
		var wheels = carLoader.WheelsAlignment;
		wheels.FL = target.FL; wheels.FR = target.FR; wheels.RL = target.RL; wheels.RR = target.RR;
		carLoader.WheelsAlignment = wheels;
		var left = carLoader.HeadlampLeftAlignment;
		left.Horizontal = target.LampLH; left.Vertical = target.LampLV;
		carLoader.HeadlampLeftAlignment = left;
		var right = carLoader.HeadlampRightAlignment;
		right.Horizontal = target.LampRH; right.Vertical = target.LampRV;
		carLoader.HeadlampRightAlignment = right;
	}

	private static void ApplyTuning(CarLoader carLoader, ModCarTuning tuning)
	{
		if (tuning == null) return;
		var gearbox = tuning.Gearbox == null ? null : carLoader.GetRoot()?.GetComponentInChildren<GearboxHandle>();
		if (gearbox != null)
		{
			if (tuning.Gearbox.GearRatio != null) gearbox.gearRatio = tuning.Gearbox.GearRatio;
			gearbox.finalDriveRatio = tuning.Gearbox.FinalDriveRatio;
		}
		if (tuning.Modules == null || tuning.Modules.Count == 0) return;
		var modules = Modules(carLoader);
		foreach (var entry in tuning.Modules)
		{
			if (entry.Data == null || !modules.TryGetValue(entry.PartKey ?? "", out var module)) continue;
			var values = module.GetValues();
			bool same = module.IsTuned() == entry.Data.IsTuned && Math.Abs(module.data.TuningValue - entry.Data.TuningValue) < 0.0005f
			            && Enumerable.SequenceEqual(values == null ? Array.Empty<short>() : (short[])values, entry.Data.Values ?? Array.Empty<short>());
			if (same) continue;
			if (entry.Data.IsTuned)
				module.Tune(entry.Data.Values ?? Array.Empty<short>(), entry.Data.TuningValue);
			else
			{
				var data = new CMS.Containers.TuningData { IsTuned = false, Values = entry.Data.Values ?? Array.Empty<short>(), TuningValue = entry.Data.TuningValue };
				module.CopyDataFrom(ref data);
			}
		}
	}

	private static void ApplyPaint(CarLoader carLoader, ModCarPaint paint)
	{
		if (paint == null) return;
		if (paint.FactoryColor != null) carLoader.SetFactoryColor(ToGame(paint.FactoryColor));
		carLoader.SetFactoryPaintType((PaintType)(int)paint.FactoryPaintType);
		if (paint.Color != null) carLoader.color = ToGame(paint.Color);
		if (paint.IsCustom && (!carLoader.IsCustomPaintType || !Same(carLoader.GetPaintData(), paint.PaintData)))
			carLoader.SetCustomCarPaintType(ToGame(paint.PaintData));
		else if (!paint.IsCustom && carLoader.IsCustomPaintType)
			carLoader.IsCustomPaintType = false;
	}

	private static void ApplyCosmetics(CarLoader carLoader, List<ModBodyCosmetics> cosmetics)
	{
		if (cosmetics == null) return;
		var parts = carLoader.carParts;
		foreach (var entry in cosmetics)
		{
			if (parts == null || entry.PartIndex < 0 || entry.PartIndex >= parts.Count) continue;
			var part = parts[entry.PartIndex];
			if (entry.Color != null && !Same(part.Color, entry.Color)) carLoader.SetCarColor(part, ToGame(entry.Color));
			if (entry.PaintType == ModPaintType.Custom)
			{
				if (part.PaintType != PaintType.Custom || !Same(part.PaintData, entry.PaintData)) carLoader.SetCustomCarPaintType(part, ToGame(entry.PaintData));
			}
			else if ((int)part.PaintType != (int)entry.PaintType) carLoader.SetCarPaintType(part, (PaintType)(int)entry.PaintType);
			if ((entry.Livery ?? "") != (part.Livery ?? "") || Math.Abs(part.LiveryStrength - entry.LiveryStrength) > 0.0005f)
				carLoader.SetCarLivery(part, entry.Livery ?? "", entry.LiveryStrength);
			if (Math.Abs(part.Dust - entry.Dust) > 0.0005f) carLoader.EnableDust(part, entry.Dust);
			if (Math.Abs(part.WashFactor - entry.WashFactor) > 0.0005f) carLoader.SetWashFactor(part, entry.WashFactor);
			if (entry.TintColor != null && (part.IsTinted != entry.IsTinted || !Same(part.TintColor, entry.TintColor)))
			{
				var tint = ToGame(entry.TintColor);
				part.SetColorAndOpacity(tint, Mathf.RoundToInt(tint.a * 255f), entry.IsTinted);
				if (IsTintableWindow(part)) PaintHelper.SetWindowProperties(part.handle, tint);
			}
		}
	}

	// The game's own window list (WindowTintManager.PrepareWindowsArray); other parts' renderers can hold null
	// materials, on which SetWindowProperties throws.
	private static bool IsTintableWindow(CarPart part) =>
		part.handle != null && !part.Unmounted && part.name != null && part.name.Contains("window");

	private static void ApplyPlates(CarLoader carLoader, ModLPData plates)
	{
		if (plates == null) return;
		var local = carLoader.LicensePlatesData;
		if (plates.LicensePlateNumberFront != null && plates.LicensePlateNumberFront != local.LicensePlateNumberFront)
			carLoader.SetNewLicensePlateNumber(plates.LicensePlateNumberFront, true);
		if (plates.LicensePlateNumberRear != null && plates.LicensePlateNumberRear != local.LicensePlateNumberRear)
			carLoader.SetNewLicensePlateNumber(plates.LicensePlateNumberRear, false);
		if (plates.FactoryLicensePlateNumber != null && plates.FactoryLicensePlateNumber != carLoader.LicensePlatesData.FactoryLicensePlateNumber)
		{
			var data = carLoader.LicensePlatesData;
			data.FactoryLicensePlateNumber = plates.FactoryLicensePlateNumber;
			carLoader.LicensePlatesData = data;
		}
		ApplyPlateTexture(carLoader, PlateFront, plates.LicensePlateFrontTex, true);
		ApplyPlateTexture(carLoader, PlateRear, plates.LicensePlateRearTex, false);
	}

	public const string PlateFront = "license_plate_front";
	public const string PlateRear = "license_plate_rear";

	public static void ApplyPlateTexture(CarLoader carLoader, string partName, string texture, bool front)
	{
		if (string.IsNullOrEmpty(texture) || texture == PlateTexture(carLoader, front)) return;
		foreach (var part in carLoader.carParts)
		{
			if (part == null || part.name != partName) continue;
			carLoader.ChangeLicencePlateTexture(part, texture);
			break;
		}
		if (texture == PlateTexture(carLoader, front)) return;
		var data = carLoader.LicensePlatesData;
		if (front) data.LicensePlateFrontTex = texture;
		else data.LicensePlateRearTex = texture;
		carLoader.LicensePlatesData = data;
	}

	private static string PlateTexture(CarLoader carLoader, bool front) =>
		front ? carLoader.LicensePlatesData.LicensePlateFrontTex : carLoader.LicensePlatesData.LicensePlateRearTex;

	private static void ApplyInfo(CarLoader carLoader, ModCarInfo info)
	{
		if (info == null) return;
		var data = carLoader.CarInfoData;
		data.Mileage = info.Mileage;
		data.BuyPrice = info.BuyPrice;
		data.CarFrom = (CarFrom)(int)info.CarFrom;
		carLoader.CarInfoData = data;
	}

	private static void ApplyDyno(CarLoader carLoader, ModDynoResult dyno)
	{
		if (dyno?.Engine == null || Away.DynoSync.IsOpenOn(carLoader)) return;
		carLoader.EngineData = ToGame(dyno.Engine);
		carLoader.MeasuredDragIndex = dyno.MeasuredDragIndex;
	}

	private static ModEngineData ToMod(EngineData engine) => new ModEngineData
	{
		IsElectric = engine.isElectric, IdleRpm = engine.idleRpm, IdleRpmTorque = engine.idleRpmTorque, IdleRpmCurveBias = engine.idleRpmCurveBias,
		PeakRpm = engine.peakRpm, PeakRpmTorque = engine.peakRpmTorque, PeakRpmCurveBias = engine.peakRpmCurveBias, MaxRpm = engine.maxRpm,
		Inertia = engine.inertia, EngineFrictionTorque = engine.engineFrictionTorque, EngineFrictionRotational = engine.engineFrictionRotational,
		EngineFrictionViscous = engine.engineFrictionViscous, LimiterTriggerRpm = engine.limiterTriggerRpm, TuningValue = engine.tuningValue,
		Measured = engine.measured,
	};

	private static EngineData ToGame(ModEngineData engine) => new EngineData
	{
		isElectric = engine.IsElectric, idleRpm = engine.IdleRpm, idleRpmTorque = engine.IdleRpmTorque, idleRpmCurveBias = engine.IdleRpmCurveBias,
		peakRpm = engine.PeakRpm, peakRpmTorque = engine.PeakRpmTorque, peakRpmCurveBias = engine.PeakRpmCurveBias, maxRpm = engine.MaxRpm,
		inertia = engine.Inertia, engineFrictionTorque = engine.EngineFrictionTorque, engineFrictionRotational = engine.EngineFrictionRotational,
		engineFrictionViscous = engine.EngineFrictionViscous, limiterTriggerRpm = engine.LimiterTriggerRpm, tuningValue = engine.TuningValue,
		measured = engine.Measured,
	};

	private static bool Same(Color color, ModColor mod) =>
		Math.Abs(color.r - mod.r) < 0.002f && Math.Abs(color.g - mod.g) < 0.002f && Math.Abs(color.b - mod.b) < 0.002f && Math.Abs(color.a - mod.a) < 0.002f;

	private static bool Same(PaintData data, ModPaintData mod) =>
		Math.Abs(data.Metal - mod.metal) < 0.002f && Math.Abs(data.Roughness - mod.roughness) < 0.002f && Math.Abs(data.ClearCoat - mod.clearCoat) < 0.002f
		&& Math.Abs(data.NormalStrength - mod.normalStrenght) < 0.002f && Math.Abs(data.Fresnel - mod.fresnel) < 0.002f;

	private static ModPaintData ToMod(PaintData data) =>
		new ModPaintData { metal = data.Metal, roughness = data.Roughness, clearCoat = data.ClearCoat, normalStrenght = data.NormalStrength, fresnel = data.Fresnel };

	private static PaintData ToGame(ModPaintData data) =>
		new PaintData { Metal = data.metal, Roughness = data.roughness, ClearCoat = data.clearCoat, NormalStrength = data.normalStrenght, Fresnel = data.fresnel };

	private static ModColor ToMod(Color color) => new ModColor { r = color.r, g = color.g, b = color.b, a = color.a };

	private static Color ToGame(ModColor color) => new Color(color.r, color.g, color.b, color.a);
}
