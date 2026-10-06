using System;
using System.Collections.Generic;
using CMS21_Together_Core.Data.GameType;
using CMS21Together.Logic.Car.Details;

namespace TogetherTestHarness.Features;

public static class CarDetailsCommands
{
    private static readonly CarDetailSection[] Sections =
    {
        CarDetailSection.Fluids, CarDetailSection.Wheels, CarDetailSection.Alignment, CarDetailSection.Tuning, CarDetailSection.Paint,
        CarDetailSection.BodyCosmetics, CarDetailSection.Plates, CarDetailSection.Info,
    };

    [HarnessCommand("cardetails-show")]
    private static object Show(string args)
    {
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse((args ?? "").Trim()));
        if (carLoader == null || !carLoader.IsCarLoaded()) throw new ArgumentException("no loaded car");
        var details = CarDetailsIO.Read(carLoader, CarDetailsIO.All);
        var result = new Dictionary<string, object>();
        foreach (var section in Sections)
        {
            string signature = CarDetailsSync.Signature(details, section);
            result[section.ToString()] = signature.Length > 400 ? $"{signature.Length}:{signature.GetHashCode():X8}" : signature;
        }
        return result;
    }

    [HarnessCommand("cardetails-randomize")]
    private static object Randomize(string args)
    {
        var carLoader = CarLoaderPlaces.Get().GetCarLoaderByIndex(int.Parse((args ?? "").Trim()));
        if (carLoader == null || !carLoader.IsCarLoaded()) throw new ArgumentException("no loaded car");
        var random = new Random();
        var details = CarDetailsIO.Read(carLoader, CarDetailsIO.All);
        foreach (var fluid in details.Fluids) fluid.Level = (float)Math.Round(random.NextDouble(), 2);
        details.Alignment.FL = (float)Math.Round(random.NextDouble() * 2 - 1, 2);
        details.Info.Mileage += random.Next(1000, 9000);
        foreach (var part in details.BodyCosmetics) part.Dust = (float)Math.Round(random.NextDouble(), 2);
        int tinted = 0;
        for (int i = 0; i < details.BodyCosmetics.Count && i < carLoader.carParts.Count; i++)
        {
            string name = carLoader.carParts[i].name.ToLowerInvariant();
            if (!name.Contains("szyb") && !name.Contains("window")) continue;
            details.BodyCosmetics[i].IsTinted = true;
            details.BodyCosmetics[i].TintColor = new ModColor { r = 0.1f, g = 0.1f, b = 0.1f, a = (float)Math.Round(random.Next(80, 200) / 255.0, 3) };
            tinted++;
        }
        details.Plates.LicensePlateNumberFront = $"TST {random.Next(100, 999)}";
        int painted = -1;
        for (int i = 0; i < details.BodyCosmetics.Count && painted < 0; i++)
        {
            if (details.BodyCosmetics[i].IsTinted) continue;
            painted = i;
            details.BodyCosmetics[i].Color = new ModColor { r = (float)Math.Round(random.NextDouble(), 2), g = 0.2f, b = 0.6f, a = 1f };
            details.BodyCosmetics[i].PaintType = ModPaintType.Matt;
        }
        foreach (var module in details.Tuning.Modules)
        {
            module.Data.IsTuned = true;
            module.Data.TuningValue = (float)Math.Round(random.NextDouble(), 2);
            if (module.Data.Values != null)
                for (int v = 0; v < module.Data.Values.Length; v++) module.Data.Values[v] = (short)random.Next(0, 100);
        }
        CarDetailsIO.Apply(carLoader, details);
        CarDetailsSync.MarkDirty(carLoader, CarDetailSection.BodyCosmetics | CarDetailSection.Plates | CarDetailSection.Tuning);
        return new { mileage = details.Info.Mileage, plate = details.Plates.LicensePlateNumberFront, tinted, painted, tuned = details.Tuning.Modules.Count };
    }

    [HarnessCommand("cardetails-hold")]
    private static object Hold(string args)
    {
        CarDetailsSync.HoldSpawnSnapshots = (args ?? "").Trim() == "on";
        return new { held = CarDetailsSync.HoldSpawnSnapshots };
    }
}
