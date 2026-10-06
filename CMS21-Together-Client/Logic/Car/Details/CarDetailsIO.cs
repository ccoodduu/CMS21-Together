using System;
using System.Collections.Generic;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Logging;
using UnityEngine;

namespace CMS21Together.Logic.Car.Details;

// Reads car details from a CarLoader and applies remote ones with side-effect-free setters
// (docs/spikes/car-details.md "Side-effect-free apply"; the order follows sync-car-details D10).
public static class CarDetailsIO
{
	public const CarDetailSection Polled = CarDetailSection.Fluids | CarDetailSection.Wheels | CarDetailSection.Alignment | CarDetailSection.Info;
	public const CarDetailSection All = Polled | CarDetailSection.Tuning | CarDetailSection.Paint | CarDetailSection.BodyCosmetics | CarDetailSection.Plates;

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
			details.Paint = new ModCarPaint { Color = ToMod(carLoader.color), FactoryColor = ToMod(carLoader.factoryColor), FactoryPaintType = (ModPaintType)(int)carLoader.factoryPaintType, IsCustom = carLoader.IsCustomPaintType };
		if (sections.HasFlag(CarDetailSection.BodyCosmetics)) details.BodyCosmetics = ReadCosmetics(carLoader);
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
		return tuning;
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
				PartIndex = i, Color = ToMod(part.Color), PaintType = (ModPaintType)(int)part.PaintType, Livery = part.Livery, LiveryStrength = part.LiveryStrength,
				IsTinted = part.IsTinted, TintColor = ToMod(part.TintColor), Dust = part.Dust, WashFactor = part.WashFactor,
			});
		}
		return result;
	}

	public static void Apply(CarLoader carLoader, ModCarDetails details)
	{
		Try("wheels", () => ApplyWheels(carLoader, details.Wheels));
		Try("tuning", () => ApplyTuning(carLoader, details.Tuning));
		Try("fluids", () => ApplyFluids(carLoader, details.Fluids));
		Try("alignment", () => ApplyAlignment(carLoader, details.Alignment));
		Try("paint", () => ApplyPaint(carLoader, details.Paint));
		Try("cosmetics", () => ApplyCosmetics(carLoader, details.BodyCosmetics));
		Try("plates", () => ApplyPlates(carLoader, details.Plates));
		Try("info", () => ApplyInfo(carLoader, details.Info));
	}

	private static void Try(string what, Action apply)
	{
		try { apply(); }
		catch (Exception e) { Log.Error($"[CarDetails] Applying {what} failed: {e.Message}"); }
	}

	private static void ApplyFluids(CarLoader carLoader, List<ModFluidLevel> fluids)
	{
		if (fluids == null) return;
		var data = carLoader.FluidsData;
		foreach (var fluid in fluids)
			data.SetLevelAndCondition(fluid.Level, fluid.Condition, (CarFluidType)(int)fluid.Type, fluid.Id);
	}

	private static void ApplyWheels(CarLoader carLoader, ModCarWheel[] wheels)
	{
		if (wheels == null) return;
		var local = carLoader.WheelsData?.Wheels;
		bool front = false, rear = false;
		for (int i = 0; local != null && i < wheels.Length && i < local.Length; i++)
		{
			var wheel = wheels[i];
			if ((int)local[i].Width == wheel.Width && (int)local[i].Size == wheel.RimSize && (int)local[i].Profile == wheel.TireSize && local[i].ET == wheel.ET) continue;
			carLoader.SetET((WheelType)i, wheel.ET);
			carLoader.SetWheelSize(wheel.Width, wheel.RimSize, wheel.TireSize, (WheelType)i);
			if (i < 2) front = true; else rear = true;
		}
		if (front) carLoader.UpdateWheels(true);
		if (rear) carLoader.UpdateWheels(false);
	}

	private static void ApplyAlignment(CarLoader carLoader, ModAlignment alignment)
	{
		if (alignment == null) return;
		var wheels = carLoader.WheelsAlignment;
		wheels.FL = alignment.FL; wheels.FR = alignment.FR; wheels.RL = alignment.RL; wheels.RR = alignment.RR;
		carLoader.WheelsAlignment = wheels;
		var left = carLoader.HeadlampLeftAlignment;
		left.Horizontal = alignment.LampLH; left.Vertical = alignment.LampLV;
		carLoader.HeadlampLeftAlignment = left;
		var right = carLoader.HeadlampRightAlignment;
		right.Horizontal = alignment.LampRH; right.Vertical = alignment.LampRV;
		carLoader.HeadlampRightAlignment = right;
	}

	private static void ApplyTuning(CarLoader carLoader, ModCarTuning tuning)
	{
		if (tuning?.Gearbox == null) return;
		var gearbox = carLoader.GetRoot()?.GetComponentInChildren<GearboxHandle>();
		if (gearbox == null) return;
		if (tuning.Gearbox.GearRatio != null) gearbox.gearRatio = tuning.Gearbox.GearRatio;
		gearbox.finalDriveRatio = tuning.Gearbox.FinalDriveRatio;
	}

	private static void ApplyPaint(CarLoader carLoader, ModCarPaint paint)
	{
		if (paint == null) return;
		if (paint.FactoryColor != null) carLoader.SetFactoryColor(ToGame(paint.FactoryColor));
		carLoader.SetFactoryPaintType((PaintType)(int)paint.FactoryPaintType);
		if (paint.Color != null) carLoader.color = ToGame(paint.Color);
	}

	private static void ApplyCosmetics(CarLoader carLoader, List<ModBodyCosmetics> cosmetics)
	{
		if (cosmetics == null) return;
		var parts = carLoader.carParts;
		foreach (var entry in cosmetics)
		{
			if (parts == null || entry.PartIndex < 0 || entry.PartIndex >= parts.Count) continue;
			var part = parts[entry.PartIndex];
			if (Math.Abs(part.Dust - entry.Dust) > 0.0005f) carLoader.EnableDust(part, entry.Dust);
			if (Math.Abs(part.WashFactor - entry.WashFactor) > 0.0005f) carLoader.SetWashFactor(part, entry.WashFactor);
		}
	}

	private static void ApplyPlates(CarLoader carLoader, ModLPData plates)
	{
		if (plates == null) return;
		var local = carLoader.LicensePlatesData;
		if (plates.LicensePlateNumberFront != null && plates.LicensePlateNumberFront != local.LicensePlateNumberFront)
			carLoader.SetNewLicensePlateNumber(plates.LicensePlateNumberFront, true);
		if (plates.LicensePlateNumberRear != null && plates.LicensePlateNumberRear != local.LicensePlateNumberRear)
			carLoader.SetNewLicensePlateNumber(plates.LicensePlateNumberRear, false);
	}

	private static void ApplyInfo(CarLoader carLoader, ModCarInfo info)
	{
		if (info == null) return;
		var data = carLoader.CarInfoData;
		data.Mileage = info.Mileage;
		data.BuyPrice = info.BuyPrice;
		data.CarFrom = (CarFrom)(int)info.CarFrom;
		carLoader.CarInfoData = data;
	}

	private static ModColor ToMod(Color color) => new ModColor { r = color.r, g = color.g, b = color.b, a = color.a };

	private static Color ToGame(ModColor color) => new Color(color.r, color.g, color.b, color.a);
}
