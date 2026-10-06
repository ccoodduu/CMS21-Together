using CMS21_Together_Core.Data.GameType;
using UnhollowerBaseLib;
using UnityEngine;

namespace CMS21Together.Logic.Jobs;

public static class ModJobConverter
{
	public static ModJob ToMod(Job job)
	{
		if (job == null) return null;
		var mod = new ModJob
		{
			id = job.id, carLoaderID = job.carLoaderID, forXP = job.forXP, carFile = job.carFile, configVersion = job.configVersion,
			timeToEnd = job.timeToEnd, PaintType = (ModPaintType)(int)job.PaintType, carColor = ToMod(job.carColor),
			carFactoryColor = ToMod(job.carFactoryColor), carFactoryPaintType = (ModPaintType)(int)job.carFactoryPaintType,
			Mileage = job.Mileage, oilLevel = job.oilLevel, jobPartsCount = job.jobPartsCount, globalCondition = job.globalCondition,
			otherPartsCondition = job.otherPartsCondition, BonusToExp = job.BonusToExp, BonusToMoney = job.BonusToMoney,
			LocalizationID = job.LocalizationID, IsMission = job.IsMission, MissionID = job.MissionID, CanDelete = job.CanDelete,
			TaskBonus = job.TaskBonus, JobBonus = job.JobBonus, TotalPayout = job.TotalPayout, MoneySpent = job.MoneySpent,
			MoneySpentWithDifficultyMod = job.MoneySpentWithDifficultyMod, XP = job.XP, IsCompleted = job.IsCompleted,
			IconTypeEngine = job.IconTypeEngine, IconTypeTiming = job.IconTypeTiming, IconTypeSuspension = job.IconTypeSuspension,
			IconTypeBrakes = job.IconTypeBrakes, IconTypeExhaust = job.IconTypeExhaust, IconTypeGearbox = job.IconTypeGearbox,
			IconTypeOil = job.IconTypeOil, IconTypeBody = job.IconTypeBody, IconTypeTuning = job.IconTypeTuning,
		};
		if (job.jobType != null)
		{
			mod.jobType = new bool[job.jobType.Length];
			for (int i = 0; i < job.jobType.Length; i++) mod.jobType[i] = job.jobType[i];
		}
		for (int i = 0; job.jobTasks != null && i < job.jobTasks.Length; i++)
		{
			var task = job.jobTasks[i];
			var modTask = new ModJobTask
			{
				type = task.type, subtype = task.subtype, IncreaseTuneValue = task.IncreaseTuneValue, partsCount = task.partsCount,
				desc = task.desc, easyMode = task.easyMode, moneySpent = task.moneySpent, Done = task.Done,
			};
			for (int p = 0; task.Parts != null && p < task.Parts.Count; p++)
			{
				var part = task.Parts[p];
				modTask.Parts.Add(new ModJobPart { ID = part.ID, Name = part.Name, Done = part.Done, Found = part.Found });
			}
			mod.jobTasks.Add(modTask);
		}
		return mod;
	}

	public static Job ToGame(ModJob mod)
	{
		var job = new Job
		{
			id = mod.id, carLoaderID = mod.carLoaderID, forXP = mod.forXP, carFile = mod.carFile, configVersion = mod.configVersion,
			timeToEnd = mod.timeToEnd, PaintType = (PaintType)(int)mod.PaintType, carColor = ToGame(mod.carColor),
			carFactoryColor = ToGame(mod.carFactoryColor), carFactoryPaintType = (PaintType)(int)mod.carFactoryPaintType,
			Mileage = mod.Mileage, oilLevel = mod.oilLevel, jobPartsCount = mod.jobPartsCount, globalCondition = mod.globalCondition,
			otherPartsCondition = mod.otherPartsCondition, BonusToExp = mod.BonusToExp, BonusToMoney = mod.BonusToMoney,
			LocalizationID = mod.LocalizationID, IsMission = mod.IsMission, MissionID = mod.MissionID, CanDelete = mod.CanDelete,
			TaskBonus = mod.TaskBonus, JobBonus = mod.JobBonus, TotalPayout = mod.TotalPayout, MoneySpent = mod.MoneySpent,
			MoneySpentWithDifficultyMod = mod.MoneySpentWithDifficultyMod, XP = mod.XP, IsCompleted = mod.IsCompleted,
			IconTypeEngine = mod.IconTypeEngine, IconTypeTiming = mod.IconTypeTiming, IconTypeSuspension = mod.IconTypeSuspension,
			IconTypeBrakes = mod.IconTypeBrakes, IconTypeExhaust = mod.IconTypeExhaust, IconTypeGearbox = mod.IconTypeGearbox,
			IconTypeOil = mod.IconTypeOil, IconTypeBody = mod.IconTypeBody, IconTypeTuning = mod.IconTypeTuning,
		};
		if (mod.jobType != null)
		{
			job.jobType = new Il2CppStructArray<bool>(mod.jobType.Length);
			for (int i = 0; i < mod.jobType.Length; i++) job.jobType[i] = mod.jobType[i];
		}
		job.jobTasks = new Il2CppReferenceArray<JobTask>(mod.jobTasks.Count);
		for (int i = 0; i < mod.jobTasks.Count; i++)
		{
			var modTask = mod.jobTasks[i];
			var task = new JobTask
			{
				type = modTask.type, subtype = modTask.subtype, IncreaseTuneValue = modTask.IncreaseTuneValue, partsCount = modTask.partsCount,
				desc = modTask.desc, easyMode = modTask.easyMode, moneySpent = modTask.moneySpent, Done = modTask.Done,
				Parts = new Il2CppSystem.Collections.Generic.List<JobPart>(),
			};
			foreach (var modPart in modTask.Parts)
			{
				var part = new JobPart { ID = modPart.ID, Name = modPart.Name, Done = modPart.Done, Found = modPart.Found };
				task.Parts.Add(part);
			}
			job.jobTasks[i] = task;
		}
		return job;
	}

	private static ModColor ToMod(Color color) => new ModColor { r = color.r, g = color.g, b = color.b, a = color.a };

	private static Color ToGame(ModColor color) => color == null ? Color.white : new Color(color.r, color.g, color.b, color.a);
}
