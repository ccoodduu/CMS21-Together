using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CMS21_Together_Core.Data.GameType;
using CMS21Together.Logic.Car.Details;
using CMS21Together.Logic.Car.Parts;
using Newtonsoft.Json.Linq;

namespace TogetherTestHarness.Features;

public static class CarDetailsCommands
{
    private static readonly CarDetailSection[] Sections =
    {
        CarDetailSection.Fluids, CarDetailSection.Wheels, CarDetailSection.Alignment, CarDetailSection.Tuning, CarDetailSection.Paint,
        CarDetailSection.BodyCosmetics, CarDetailSection.Plates, CarDetailSection.Info, CarDetailSection.Dyno,
    };

    [HarnessCommand("cardetails-flush")]
    private static object FlushNow(string args)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1) throw new ArgumentException("usage: cardetails-flush <loader> [applying <ms>]");
        if (parts.Length == 3 && parts[1] == "applying")
            CarDetailsSync.TestApplyingUntil = UnityEngine.Time.realtimeSinceStartup + int.Parse(parts[2]) / 1000f;
        return new { result = CarDetailsSync.FlushNow(int.Parse(parts[0]), CarDetailSection.Fluids).ToString() };
    }

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

    [HarnessCommand("cardetails-plate")]
    private static object SetPlate(string args)
    {
        var parts = Split(args, 3, "cardetails-plate <loader> front|rear <texture>");
        var carLoader = Loaded(parts[0]);
        bool front = parts[1] == "front";
        CarDetailsIO.ApplyPlateTexture(carLoader, front ? CarDetailsIO.PlateFront : CarDetailsIO.PlateRear, parts[2], front);
        CarDetailsSync.MarkDirty(carLoader, CarDetailSection.Plates);
        return Entries(carLoader, CarDetailSection.Plates, CarDetailEntries.Plates);
    }

    [HarnessCommand("cardetails-fluid")]
    private static object SetFluid(string args)
    {
        var parts = Split(args, 4, "cardetails-fluid <loader> <type> <id> <level> [cond]");
        var carLoader = Loaded(parts[0]);
        var type = (ModCarFluidType)Enum.Parse(typeof(ModCarFluidType), parts[1], true);
        int id = int.Parse(parts[2]);
        var current = CarDetailsIO.Read(carLoader, CarDetailSection.Fluids).Fluids.FirstOrDefault(f => f.Type == type && f.Id == id)
                      ?? throw new ArgumentException($"no fluid {type}.{id}");
        current.Level = Float(parts[3]);
        if (parts.Length > 4) current.Condition = Float(parts[4]);
        CarDetailsIO.Apply(carLoader, new ModCarDetails { Fluids = new List<ModFluidLevel> { current } });
        CarDetailsSync.MarkDirty(carLoader, CarDetailSection.Fluids);
        return Entries(carLoader, CarDetailSection.Fluids, CarDetailEntries.Fluid(type, id));
    }

    [HarnessCommand("cardetails-wheel")]
    private static object SetWheel(string args)
    {
        var parts = Split(args, 6, "cardetails-wheel <loader> <index> <w> <rim> <tire> <et>");
        var carLoader = Loaded(parts[0]);
        int index = int.Parse(parts[1]);
        var wheels = CarDetailsIO.Read(carLoader, CarDetailSection.Wheels).Wheels;
        if (wheels == null || index < 0 || index >= wheels.Length) throw new ArgumentException($"no wheel {index}");
        wheels[index].Width = int.Parse(parts[2]);
        wheels[index].RimSize = int.Parse(parts[3]);
        wheels[index].TireSize = int.Parse(parts[4]);
        wheels[index].ET = int.Parse(parts[5]);
        CarDetailsIO.Apply(carLoader, new ModCarDetails { Wheels = wheels });
        CarDetailsSync.MarkDirty(carLoader, CarDetailSection.Wheels);
        return Entries(carLoader, CarDetailSection.Wheels, CarDetailEntries.Wheel(index));
    }

    [HarnessCommand("cardetails-alignment")]
    private static object SetAlignment(string args)
    {
        var parts = Split(args, 5, "cardetails-alignment <loader> <FL> <FR> <RL> <RR> (- leaves a field)");
        var carLoader = Loaded(parts[0]);
        var alignment = CarDetailsIO.Read(carLoader, CarDetailSection.Alignment).Alignment;
        var touched = new List<string>();
        for (int field = 0; field < 4; field++)
        {
            if (parts[field + 1] == "-") continue;
            CarDetailEntries.SetAlignmentValue(alignment, field, Float(parts[field + 1]));
            touched.Add(CarDetailEntries.Alignment(CarDetailEntries.AlignmentFieldNames[field]));
        }
        CarDetailsIO.Apply(carLoader, new ModCarDetails { Alignment = alignment });
        CarDetailsSync.MarkDirty(carLoader, CarDetailSection.Alignment);
        return Entries(carLoader, CarDetailSection.Alignment, touched.ToArray());
    }

    [HarnessCommand("cardetails-wash")]
    private static object SetWash(string args)
    {
        var parts = Split(args, 3, "cardetails-wash <loader> <dust> <wash> [panelIndex]");
        var carLoader = Loaded(parts[0]);
        var panels = CarDetailsIO.Read(carLoader, CarDetailSection.BodyCosmetics).BodyCosmetics;
        if (parts.Length > 3)
        {
            int index = int.Parse(parts[3]);
            panels = panels.Where(p => p.PartIndex == index).ToList();
            if (panels.Count == 0) throw new ArgumentException($"no panel {index}");
        }
        foreach (var panel in panels)
        {
            panel.Dust = Float(parts[1]);
            panel.WashFactor = Float(parts[2]);
        }
        CarDetailsIO.Apply(carLoader, new ModCarDetails { BodyCosmetics = panels });
        CarDetailsSync.MarkDirty(carLoader, CarDetailSection.BodyCosmetics);
        var result = Entries(carLoader, CarDetailSection.BodyCosmetics, panels.Select(p => CarDetailEntries.Cosmetics(p.PartIndex)).Take(3).ToArray());
        result["panels"] = panels.Count;
        return result;
    }

    private class Pour
    {
        public float Start;
        public float End;
        public int Decreases;
        public int Frames;
        public bool Done;
    }

    private static Pour lastPour;

    // FluidRefillLogic.Update's own steps while the button is held: AddFluid on the car's field every frame, level by
    // deltaTime * 0.1 and condition by deltaTime * 0.05, with no amount of its own.
    [HarnessCommand("cardetails-pour")]
    private static object PourCommand(string args)
    {
        if ((args ?? "").Trim() == "result")
        {
            if (lastPour == null) throw new InvalidOperationException("no pour");
            return new { start = lastPour.Start, end = lastPour.End, decreases = lastPour.Decreases, frames = lastPour.Frames, done = lastPour.Done };
        }
        var parts = Split(args, 4, "cardetails-pour <loader> <type> <id> <seconds> | result");
        var carLoader = Loaded(parts[0]);
        var type = (CarFluidType)(int)(ModCarFluidType)Enum.Parse(typeof(ModCarFluidType), parts[1], true);
        int id = int.Parse(parts[2]);
        lastPour = new Pour { Start = carLoader.FluidsData.GetLevel(type, id, false) };
        MelonLoader.MelonCoroutines.Start(RunPour(carLoader, type, id, Float(parts[3]), lastPour));
        return new { start = lastPour.Start };
    }

    private static System.Collections.IEnumerator RunPour(CarLoader carLoader, CarFluidType type, int id, float seconds, Pour pour)
    {
        float until = UnityEngine.Time.realtimeSinceStartup + seconds;
        float last = pour.Start;
        CarDetailsSync.MarkDirty(carLoader, CarDetailSection.Fluids);
        while (UnityEngine.Time.realtimeSinceStartup < until && carLoader != null)
        {
            var fluids = carLoader.FluidsData;
            float level = fluids.GetLevel(type, id, false);
            if (level < last - 0.0001f) pour.Decreases++;
            float dt = UnityEngine.Time.deltaTime;
            fluids.AddFluid(dt * 0.1f, dt * 0.05f, type, id);
            last = fluids.GetLevel(type, id, false);
            pour.Frames++;
            yield return null;
        }
        pour.End = carLoader == null ? 0f : carLoader.FluidsData.GetLevel(type, id, false);
        pour.Done = true;
        if (carLoader != null) CarDetailsSync.MarkDirty(carLoader, CarDetailSection.Fluids);
    }

    public static object Dump()
    {
        var places = CarLoaderPlaces.Get();
        if (places == null) return null;
        var result = new List<object>();
        foreach (var sync in CarPartsSync.All.Where(s => s.State == LoaderSyncState.Ready).OrderBy(s => s.Loader))
        {
            var carLoader = places.GetCarLoaderByIndex(sync.Loader);
            if (carLoader == null || !carLoader.IsCarLoaded()) continue;
            var entries = new SortedDictionary<string, object>(StringComparer.Ordinal);
            foreach (var pair in CarDetailEntries.Signatures(CarDetailsIO.Read(carLoader, CarDetailsIO.All)))
                entries[pair.Key] = JToken.Parse(pair.Value);
            result.Add(new { loader = sync.Loader, entries });
        }
        return result;
    }

    private static Dictionary<string, object> Entries(CarLoader carLoader, CarDetailSection section, params string[] ids)
    {
        var signatures = CarDetailEntries.Signatures(CarDetailsIO.Read(carLoader, section));
        var result = new Dictionary<string, object>();
        foreach (string id in ids) result[id] = signatures.TryGetValue(id, out string value) ? JToken.Parse(value) : null;
        return result;
    }

    private static string[] Split(string args, int minimum, string usage)
    {
        var parts = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < minimum) throw new ArgumentException($"usage: {usage}");
        return parts;
    }

    private static CarLoader Loaded(string index)
    {
        var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(int.Parse(index));
        if (carLoader == null || !carLoader.IsCarLoaded()) throw new ArgumentException("no loaded car");
        return carLoader;
    }

    private static float Float(string value) => float.Parse(value, CultureInfo.InvariantCulture);

    [HarnessCommand("cardetails-hold")]
    private static object Hold(string args)
    {
        CarDetailsSync.HoldSpawnSnapshots = (args ?? "").Trim() == "on";
        return new { held = CarDetailsSync.HoldSpawnSnapshots };
    }

    internal static void Reset(List<string> changed)
    {
        if (CarDetailsSync.HoldSpawnSnapshots) changed.Add("cardetails-hold");
        CarDetailsSync.HoldSpawnSnapshots = false;
    }
}
