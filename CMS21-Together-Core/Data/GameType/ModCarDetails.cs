using System;
using System.Collections.Generic;

namespace CMS21_Together_Core.Data.GameType;

[Flags]
public enum CarDetailSection
{
	None = 0,
	Fluids = 1,
	Wheels = 2,
	Alignment = 4,
	Tuning = 8,
	Paint = 16,
	BodyCosmetics = 32,
	Plates = 64,
	Info = 128,
	BonusParts = 256,
	Dyno = 512
}

public enum ModCarFluidType
{
	None,
	All,
	Brake,
	EngineOil,
	EngineCoolant,
	WindscreenWash,
	PowerSteering
}

public enum ModCarFrom
{
	None,
	Junkyard,
	Barn,
	Order,
	Mission,
	Auction,
	Salon
}

[Serializable]
public class ModCarDetails
{
	public int SpawnSeq;
	public bool HasSnapshot;
	public List<ModFluidLevel> Fluids;
	public ModCarWheel[] Wheels;
	public ModAlignment Alignment;
	public ModCarTuning Tuning;
	public ModCarPaint Paint;
	public List<ModBodyCosmetics> BodyCosmetics;
	public ModLPData Plates;
	public ModCarInfo Info;
	public ModBonusParts BonusParts;
	public ModDynoResult Dyno;
}

[Serializable]
public class ModFluidLevel
{
	public ModCarFluidType Type;
	public int Id;
	public float Level;
	public float Condition;
}

[Serializable]
public class ModCarWheel
{
	public int Width;
	public int RimSize;
	public int TireSize;
	public int ET;
	public string Tire;
	public string Rim;
}

[Serializable]
public class ModAlignment
{
	public float FL, FR, RL, RR;
	public float LampLH, LampLV, LampRH, LampRV;
}

[Serializable]
public class ModCarTuning
{
	public string GearboxPartKey;
	public ModGearboxData Gearbox;
	public List<ModPartTuning> Modules = new List<ModPartTuning>();
}

[Serializable]
public class ModPartTuning
{
	public string PartKey;
	public ModTuningData Data;
	public int EcuStage = -1;
}

[Serializable]
public class ModCarPaint
{
	public ModColor Color;
	public ModColor FactoryColor;
	public ModPaintType FactoryPaintType;
	public ModPaintData PaintData;
	public bool IsCustom;
}

[Serializable]
public class ModBodyCosmetics
{
	public int PartIndex;
	public ModColor Color;
	public ModPaintType PaintType;
	public ModPaintData PaintData;
	public string Livery;
	public float LiveryStrength;
	public bool IsTinted;
	public ModColor TintColor;
	public float Dust;
	public float WashFactor;
}

[Serializable]
public class ModCarInfo
{
	public int Mileage;
	public int BuyPrice;
	public ModCarFrom CarFrom;
}

[Serializable]
public class ModBonusParts
{
	public string[] IDs;
	public bool IsPainted;
	public ModColor Color;
	public ModPaintType PaintType;
	public ModPaintData PaintData;
}

[Serializable]
public class ModEngineData
{
	public bool IsElectric;
	public float IdleRpm;
	public float IdleRpmTorque;
	public float IdleRpmCurveBias;
	public float PeakRpm;
	public float PeakRpmTorque;
	public float PeakRpmCurveBias;
	public float MaxRpm;
	public float Inertia;
	public float EngineFrictionTorque;
	public float EngineFrictionRotational;
	public float EngineFrictionViscous;
	public float LimiterTriggerRpm;
	public float TuningValue;
	public bool Measured;
}

[Serializable]
public class ModDynoResult
{
	public ModEngineData Engine;
	public int MeasuredDragIndex;
}
