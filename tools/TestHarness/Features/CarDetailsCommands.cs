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
            result[section.ToString()] = signature.Length > 160 ? $"{signature.Length}:{signature.GetHashCode():X8}" : signature;
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
        details.Plates.LicensePlateNumberFront = $"TST {random.Next(100, 999)}";
        CarDetailsIO.Apply(carLoader, details);
        CarDetailsSync.MarkDirty(carLoader, CarDetailSection.BodyCosmetics | CarDetailSection.Plates);
        return new { mileage = details.Info.Mileage, plate = details.Plates.LicensePlateNumberFront };
    }
}
