using System;
using CMS21_Together_Core.Network.Packets;
using Il2CppSystem.IO;

namespace CMS21Together.Logic.Car;

public static class NewCarDataCodec
{
	public static byte SaveVersion => GlobalData.CurrentSaveVersion;

	public static byte[] Serialize(NewCarData data)
	{
		var stream = new MemoryStream();
		var writer = new BinaryWriter(stream);
		data.Serialize(writer, SaveVersion);
		writer.Flush();
		return stream.ToArray();
	}

	public static NewCarData Deserialize(byte[] bytes, byte saveVersion)
	{
		var reader = new BinaryReader(new MemoryStream(bytes));
		var data = new NewCarData();
		data.BodyPartsData = new Il2CppSystem.Collections.Generic.List<BodyPartData>();
		data.PartData = new Il2CppSystem.Collections.Generic.List<PartData>();
		var fluids = data.FluidsData;
		fluids.Oil = new FluidData();
		fluids.Brake = new Il2CppSystem.Collections.Generic.List<FluidData>();
		fluids.EngineCoolant = new Il2CppSystem.Collections.Generic.List<FluidData>();
		fluids.PowerSteering = new Il2CppSystem.Collections.Generic.List<FluidData>();
		fluids.WindscreenWash = new Il2CppSystem.Collections.Generic.List<FluidData>();
		data.FluidsData = fluids;
		data.Deserialize(reader, saveVersion);
		return data;
	}

	public static ParkedCar ToParkedCar(NewCarData data) => new ParkedCar
	{
		Id = Guid.Empty,
		CarToLoad = data.carToLoad,
		UId = data.UId,
		SaveVersion = SaveVersion,
		Data = Serialize(data)
	};
}
