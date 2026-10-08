using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CMS21_Together_Core.Data.GameType;

[Serializable]
public class ModJob
{
	public int id;
	public int carLoaderID;
	public int forXP;
	public string carFile;
	public int configVersion;
	public float timeToEnd;
	public ModPaintType PaintType;
	public ModColor carColor;
	public ModColor carFactoryColor;
	public ModPaintType carFactoryPaintType;
	public int Mileage;
	public float oilLevel;
	public bool[] jobType;
	public List<ModJobTask> jobTasks = new List<ModJobTask>();
	public int jobPartsCount;
	public float globalCondition;
	public float otherPartsCondition;
	public bool BonusToExp;
	public bool BonusToMoney;
	public string LocalizationID;
	public bool IsMission;
	public int MissionID;
	public bool CanDelete;
	public int TaskBonus;
	public int JobBonus;
	public int TotalPayout;
	public int MoneySpent;
	public int MoneySpentWithDifficultyMod;
	public int XP;
	public bool IsCompleted;
	public bool IconTypeEngine;
	public bool IconTypeTiming;
	public bool IconTypeSuspension;
	public bool IconTypeBrakes;
	public bool IconTypeExhaust;
	public bool IconTypeGearbox;
	public bool IconTypeOil;
	public bool IconTypeBody;
	public bool IconTypeTuning;
	[OptionalField] public int PrepSeed;
}

[Serializable]
public class ModJobTask
{
	public string type;
	public string subtype;
	public int IncreaseTuneValue;
	public int partsCount;
	public string desc;
	public bool easyMode;
	public int moneySpent;
	public List<ModJobPart> Parts = new List<ModJobPart>();
	public bool Done;
}

[Serializable]
public class ModJobPart
{
	public string ID;
	public string Name;
	public bool Done;
	public bool Found;
}

[Serializable]
public class ModMissionState
{
	public int MissionsFinished;
	public bool CurrentMissionDone;
	public bool IsStoryMissionInProgress;
}
