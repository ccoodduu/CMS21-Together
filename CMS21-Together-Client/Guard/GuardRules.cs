using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CMS21Together.Guard;

public readonly struct GuardRule
{
	public GuardRule(GuardKind kind, string id, string owner, string label, bool allowed)
	{
		Kind = kind;
		Id = id;
		Owner = owner;
		Label = label;
		Allowed = allowed;
	}

	public GuardKind Kind { get; }
	public string Id { get; }
	public string Owner { get; }
	public string Label { get; }
	public bool Allowed { get; }
}

public static class GuardRules
{
	private const string Base = "base";
	private const string M1 = "M1";
	private const string Audit = "audit (14a)";
	private const string Never = "backlog";

	public static readonly IReadOnlyList<GuardRule> All = new[]
	{
		Allow(GuardKind.Scene, "Menu", Base, "Main menu"),
		Allow(GuardKind.Scene, "Garage", Base, "Garage"),
		Planned(GuardKind.Action, "SellCar", "row 6 part 2", "Selling cars"),
		Allow(GuardKind.Scene, "Junkyard", "row 1 (M2)", "Travel to the junkyard"),
		Planned(GuardKind.Scene, "Barn", "row 6 part 2", "Travel to a barn"),
		Planned(GuardKind.Scene, "Auction", "row 6 part 2", "Travel to the auction"),
		Planned(GuardKind.Scene, "Showroom", "row 6 part 2", "Travel to the showroom"),
		Planned(GuardKind.Scene, "Salon", "row 6 part 2", "Travel to the car salon"),
		Planned(GuardKind.Scene, "Parking", "row 6 part 2", "Travel to the parking"),
		Planned(GuardKind.Scene, "TestTrack", "row 13", "Travel to the test track"),
		Planned(GuardKind.Scene, "RaceTrack", Never, "Race track"),
		Planned(GuardKind.Scene, "FunTrack", Never, "Fun track"),
		Planned(GuardKind.Scene, "OffroadTrack", Never, "Off-road track"),
		Planned(GuardKind.Scene, "DragStrip", Never, "Drag strip"),
		Planned(GuardKind.Scene, "CustomTrack", Never, "Custom track"),
		Planned(GuardKind.Scene, "SpeedTrack", Never, "Speed track"),
		Planned(GuardKind.Scene, "PhotoLocation", Never, "Photo location"),
		Planned(GuardKind.Scene, "Tutorial", Never, "Tutorial"),

		Allow(GuardKind.Window, "AskWindow", Base, "Question"),
		Allow(GuardKind.Window, "InfoWindow", Base, "Message"),
		Allow(GuardKind.Window, "InputWindow", Base, "Text input"),
		Allow(GuardKind.Window, "PieMenu", Base, "Pie menu"),
		Allow(GuardKind.Window, "PauseQuit", Base, "Pause menu"),
		Allow(GuardKind.Window, "Settings", Base, "Settings"),
		Allow(GuardKind.Window, "Tutorials", Base, "Tutorials"),
		Allow(GuardKind.Window, "Changelog", Base, "Changelog"),
		Allow(GuardKind.Window, "Radio", Base, "Radio"),
		Allow(GuardKind.Window, "DLCError", Base, "DLC message"),
		Allow(GuardKind.Window, "Inventory", M1, "Inventory"),
		Allow(GuardKind.Window, "Sorting", M1, "Inventory sorting"),
		Allow(GuardKind.Window, "Warehouse", M1, "Warehouse"),
		Allow(GuardKind.Window, "WarehouseChange", M1, "Warehouse transfer"),
		Allow(GuardKind.Window, "Shop", M1, "Shop"),
		Allow(GuardKind.Window, "ShopBuy", M1, "Shop"),
		Allow(GuardKind.Window, "ShopList", M1, "Shopping list"),
		Allow(GuardKind.Window, "ItemsExchange", M1, "Item exchange"),
		Allow(GuardKind.Window, "SellPerCondition", M1, "Selling parts"),
		Allow(GuardKind.Window, "TakenItems", M1, "Taken items"),
		Allow(GuardKind.Window, "Upgrades", M1, "Garage upgrades"),
		Allow(GuardKind.Window, "Photo", Audit, "Photo mode"),
		Allow(GuardKind.Window, "PartInspector", "row 1", "Part inspector"),
		Allow(GuardKind.Window, "CarInfo", "row 1", "Car info"),
		Allow(GuardKind.Window, "Map", "row 1 (M2)", "The map"),
		Planned(GuardKind.Window, "CreateEngine", "row 1 (engine swap)", "Building an engine"),
		Allow(GuardKind.Window, "ChooseEngine", "row 1", "Choosing an engine for the crane"),
		Planned(GuardKind.Window, "CarLocationWindow", "row 6 part 2", "Moving cars"),
		Planned(GuardKind.Window, "Parking", "row 6 part 2", "The parking"),
		Allow(GuardKind.Window, "ParkingManagement", "row 2", "Parking management"),
		Allow(GuardKind.Window, "Orders", "row 3", "Orders"),
		Allow(GuardKind.Window, "ExamineReport", "row 3", "The examination report"),
		Planned(GuardKind.Window, "WheelsAlignment", "row 4", "Wheel alignment"),
		Planned(GuardKind.Window, "LampAlignment", "row 4", "Headlamp alignment"),
		Planned(GuardKind.Window, "Tune", "row 4", "Tuning"),
		Planned(GuardKind.Window, "Tinting", "row 4", "Window tinting"),
		Planned(GuardKind.Window, "CarVersion", "row 4", "Car version"),
		Planned(GuardKind.Window, "WheelBalance", "row 5a", "The wheel balancer"),
		Planned(GuardKind.Window, "RepairPart", "row 5a", "Repairing parts"),
		Planned(GuardKind.Window, "BrakeLathe", "row 5a", "The brake lathe"),
		Planned(GuardKind.Window, "ChoosePartUp", "row 5a", "Workshop machines"),
		Planned(GuardKind.Window, "Paintshop", "row 5b", "The paint shop"),
		Planned(GuardKind.Window, "Showroom", "row 6 part 2", "The showroom"),
		Planned(GuardKind.Window, "SalonSelectCar", "row 6 part 2", "The car salon"),
		Planned(GuardKind.Window, "SalonWizard", "row 6 part 2", "The car salon"),
		Planned(GuardKind.Window, "Auction", "row 6 part 2", "The auction"),
		Planned(GuardKind.Window, "CaseOpening", "row 10", "Opening cases"),
		Planned(GuardKind.Window, "Scrap", "row 10", "Scrapping parts"),
		Planned(GuardKind.Window, "ScrapPerCondition", "row 10", "Scrapping parts"),
		Planned(GuardKind.Window, "ShopLicenseBuy", "row 10", "Buying licenses"),
		Planned(GuardKind.Window, "PathTest", "row 13", "The test path"),
		Planned(GuardKind.Window, "Dyno", "row 13", "The dyno"),
		Planned(GuardKind.Window, "Benchmark", "row 13", "The benchmark"),
		Planned(GuardKind.Window, "NewSaveWindow", Never, "Saving"),
		Planned(GuardKind.Window, "SaveDetails", Never, "Saving"),
		Planned(GuardKind.Window, "RevertBackup", Never, "Restoring a backup"),
		Planned(GuardKind.Window, "GarageCustomization", Never, "Garage customization"),
		Planned(GuardKind.Window, "Tutorial", Never, "The tutorial"),
		Planned(GuardKind.Window, "TutorialEnd", Never, "The tutorial"),

		Allow(GuardKind.Mode, "Garage", Base, "Garage"),
		Allow(GuardKind.Mode, "UI", Base, "Windows"),
		Allow(GuardKind.Mode, "None", Base, "No mode"),
		Allow(GuardKind.Mode, "PhotoMode", Audit, "Photo mode"),
		Allow(GuardKind.Mode, "GarageDisassemble", "row 1", "Disassembly mode"),
		Allow(GuardKind.Mode, "GarageAssemble", "row 1", "Assembly mode"),
		Allow(GuardKind.Mode, "PartSelect", "row 1", "Working on parts"),
		Allow(GuardKind.Mode, "PartMount", "row 1", "Mounting parts"),
		Allow(GuardKind.Mode, "PartUnMount", "row 1", "Unmounting parts"),
		Allow(GuardKind.Mode, "GroupMount", "row 1", "Mounting parts"),
		Allow(GuardKind.Mode, "GroupUnMount", "row 1", "Unmounting parts"),
		Allow(GuardKind.Mode, "PartSelectMount", "row 1", "Mounting parts"),
		Allow(GuardKind.Mode, "InteriorDisassemble", "row 1", "Interior disassembly"),
		Allow(GuardKind.Mode, "InteriorAssemble", "row 1", "Interior assembly"),
		Allow(GuardKind.Mode, "Interior", "row 1", "Working in the interior"),
		Allow(GuardKind.Mode, "ExamineCondition", "row 1", "Examine mode"),
		Allow(GuardKind.Mode, "ExamineGarage", "row 1", "Examine mode"),
		Planned(GuardKind.Mode, "BonusAssemble", "row 4", "Bonus parts"),
		Planned(GuardKind.Mode, "BonusDisassemble", "row 4", "Bonus parts"),
		Planned(GuardKind.Mode, "DrainTool", "row 4", "Draining fluids"),
		Planned(GuardKind.Mode, "CarDrive", "row 6 part 2", "Driving"),
		Planned(GuardKind.Mode, "PathTest", "row 13", "The test path"),
		Planned(GuardKind.Mode, "Dyno", "row 13", "The dyno"),
		Planned(GuardKind.Mode, "Benchmark", "row 13", "The benchmark"),
		Planned(GuardKind.Mode, "ExamineTools", "row 13", "Diagnostic tools"),

		Allow(GuardKind.Pie, "settings", Base, "Settings"),
		Allow(GuardKind.Pie, "settings_menu", Base, "Quit to menu"),
		Allow(GuardKind.Pie, "settings_window", Base, "Settings"),
		Allow(GuardKind.Pie, "settings_tutorial", Base, "Tutorials"),
		Allow(GuardKind.Pie, "jukebox_next_sound", Base, "Radio"),
		Allow(GuardKind.Pie, "jukebox_turn_on_off", Base, "Radio"),
		Allow(GuardKind.Pie, "jukebox_change_station", Base, "Radio"),
		Allow(GuardKind.Pie, "additional_modes", Base, "More modes"),
		Allow(GuardKind.Pie, "examine_tools", Base, "Diagnostic tools"),
		Allow(GuardKind.Pie, "move_car", Base, "Moving cars"),
		Allow(GuardKind.Pie, "equipment_move", Base, "Moving equipment"),
		Allow(GuardKind.Pie, "list_options", Base, "Shopping list"),
		Allow(GuardKind.Pie, "exit_from_interior", Base, "Leaving the car"),
		Allow(GuardKind.Pie, "inventory", M1, "Inventory"),
		Allow(GuardKind.Pie, "tablet", M1, "Shop"),
		Allow(GuardKind.Pie, "list_open", M1, "Shopping list"),
		Allow(GuardKind.Pie, "list_add", M1, "Shopping list"),
		Allow(GuardKind.Pie, "settings_garage", M1, "Garage upgrades"),
		Allow(GuardKind.Pie, "mode_photo", Audit, "Photo mode"),
		Allow(GuardKind.Pie, "mode_garage", "row 1", "Disassembly mode"),
		Allow(GuardKind.Pie, "mode_garage_assemble", "row 1", "Assembly mode"),
		Allow(GuardKind.Pie, "mode_interior_disassemble", "row 1", "Interior disassembly"),
		Allow(GuardKind.Pie, "mode_interior_assemble", "row 1", "Interior assembly"),
		Allow(GuardKind.Pie, "mode_part_mount", "row 1", "Mounting parts"),
		Allow(GuardKind.Pie, "mode_part_unmount", "row 1", "Unmounting parts"),
		Allow(GuardKind.Pie, "mode_group_mount", "row 1", "Mounting parts"),
		Allow(GuardKind.Pie, "mode_group_unmount", "row 1", "Unmounting parts"),
		Allow(GuardKind.Pie, "mode_examine", "row 1", "Examine mode"),
		Allow(GuardKind.Pie, "mode_examine_garage", "row 1", "Examine mode"),
		Allow(GuardKind.Pie, "switch_body", "row 1", "Body parts"),
		Allow(GuardKind.Pie, "equipment_mount", "row 1", "The engine crane"),
		Allow(GuardKind.Pie, "equipment_unmount", "row 1", "The engine crane"),
		Allow(GuardKind.Pie, "map", "row 1 (M2)", "The map"),
		Planned(GuardKind.Pie, "engine_add", "row 1 (engine swap)", "Engine swap"),
		Planned(GuardKind.Pie, "engine_new", "row 1 (engine swap)", "Building an engine"),
		Allow(GuardKind.Pie, "move_carLift1", "row 2", "Moving cars"),
		Allow(GuardKind.Pie, "move_carLift2", "row 2", "Moving cars"),
		Allow(GuardKind.Pie, "move_entrance1", "row 2", "Moving cars"),
		Allow(GuardKind.Pie, "move_entrance2", "row 2", "Moving cars"),
		Allow(GuardKind.Pie, "move_entrance3", "row 2", "Moving cars"),
		Allow(GuardKind.Pie, "move_parking", "row 2", "Parking cars"),
		Allow(GuardKind.Pie, "move_carWash", "row 2", "Moving cars"),
		Allow(GuardKind.Pie, "move_paintshop", "row 2", "Moving cars"),
		Allow(GuardKind.Pie, "order_list", "row 3", "Orders"),
		Allow(GuardKind.Pie, "order_check", "row 3", "Checking a job"),
		Planned(GuardKind.Pie, "mode_bonus_assemble", "row 4", "Bonus parts"),
		Planned(GuardKind.Pie, "mode_bonus_disassemble", "row 4", "Bonus parts"),
		Planned(GuardKind.Pie, "drainTool", "row 4", "Draining fluids"),
		Planned(GuardKind.Pie, "balancer_balance", "row 5a", "The wheel balancer"),
		Planned(GuardKind.Pie, "balancer_take", "row 5a", "The wheel balancer"),
		Planned(GuardKind.Pie, "battery_charge", "row 5a", "The battery charger"),
		Planned(GuardKind.Pie, "battery_take", "row 5a", "The battery charger"),
		Planned(GuardKind.Pie, "brakeLathe_use", "row 5a", "The brake lathe"),
		Planned(GuardKind.Pie, "brakeLathe_take", "row 5a", "The brake lathe"),
		Planned(GuardKind.Pie, "spring_connect", "row 5a", "The spring clamp"),
		Planned(GuardKind.Pie, "spring_separate", "row 5a", "The spring clamp"),
		Planned(GuardKind.Pie, "spring_take", "row 5a", "The spring clamp"),
		Planned(GuardKind.Pie, "wheel_connect", "row 5a", "The tire changer"),
		Planned(GuardKind.Pie, "wheel_separate", "row 5a", "The tire changer"),
		Planned(GuardKind.Pie, "wheel_take", "row 5a", "The tire changer"),
		Planned(GuardKind.Pie, "engine_take", "row 5a", "The engine stand"),
		Planned(GuardKind.Pie, "engine_rotate_left", "row 5a", "The engine stand"),
		Planned(GuardKind.Pie, "engine_rotate_right", "row 5a", "The engine stand"),
		Planned(GuardKind.Pie, "paintshop_car", "row 5b", "The paint shop"),
		Planned(GuardKind.Pie, "paintshop_part", "row 5b", "The paint shop"),
		Planned(GuardKind.Pie, "equipment_use", "row 5b", "Workshop equipment"),
		Planned(GuardKind.Pie, "equipment_return", "row 5b", "Moving equipment"),
		Planned(GuardKind.Pie, "equipment_move_entrance1", "row 5b", "Moving equipment"),
		Planned(GuardKind.Pie, "equipment_move_entrance2", "row 5b", "Moving equipment"),
		Planned(GuardKind.Pie, "equipment_move_carLift1", "row 5b", "Moving equipment"),
		Planned(GuardKind.Pie, "equipment_move_carLift2", "row 5b", "Moving equipment"),
		Planned(GuardKind.Pie, "car_drive", "row 6 part 2", "Driving"),
		Planned(GuardKind.Pie, "car_run", "row 6 part 2", "Starting the engine"),
		Planned(GuardKind.Pie, "car_buy", "row 6 part 2", "Buying cars"),
		Planned(GuardKind.Pie, "start_bidding", "row 6 part 2", "The auction"),
		Planned(GuardKind.Pie, "move_pathTest", "row 13", "The test path"),
		Planned(GuardKind.Pie, "move_dyno", "row 13", "The dyno"),
		Planned(GuardKind.Pie, "obd", "row 13", "Diagnostic tools"),
		Planned(GuardKind.Pie, "cylinder", "row 13", "Diagnostic tools"),
		Planned(GuardKind.Pie, "fuel", "row 13", "Diagnostic tools"),
		Planned(GuardKind.Pie, "electronic", "row 13", "Diagnostic tools"),
		Planned(GuardKind.Pie, "tires", "row 13", "Diagnostic tools"),
		Planned(GuardKind.Pie, "settings_save", Never, "Saving"),
		Planned(GuardKind.Pie, "settings_load", Never, "Loading a save"),
		Planned(GuardKind.Pie, "settings_parking_garage", Never, "Changing garages"),
	};

	private static readonly Dictionary<string, GuardRule> byKey =
		All.ToDictionary(rule => FeatureGuard.Key(rule.Kind, rule.Id), StringComparer.OrdinalIgnoreCase);

	public static GuardRule? Find(GuardKind kind, string id) =>
		byKey.TryGetValue(FeatureGuard.Key(kind, id), out var rule) ? rule : (GuardRule?)null;

	public static bool IsAllowed(GuardKind kind, string id) => Find(kind, id)?.Allowed ?? false;

	public static string Label(GuardKind kind, string id) => Find(kind, id)?.Label ?? Readable(id);

	private static string Readable(string id)
	{
		if (string.IsNullOrEmpty(id)) return "This";
		string spaced = Regex.Replace(id.Replace('_', ' '), "(?<=[a-z])(?=[A-Z])", " ").Trim();
		return char.ToUpperInvariant(spaced[0]) + spaced.Substring(1);
	}

	private static GuardRule Allow(GuardKind kind, string id, string owner, string label) =>
		new GuardRule(kind, id, owner, label, true);

	private static GuardRule Planned(GuardKind kind, string id, string owner, string label) =>
		new GuardRule(kind, id, owner, label, false);
}
