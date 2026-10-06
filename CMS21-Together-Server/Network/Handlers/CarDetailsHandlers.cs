using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.Cars;

namespace CMS21_Together_Server.Network.Handlers
{
	public static class CarDetailsHandlers
	{
		[PacketHandler(PacketTypes.CarDetailsUpdate)]
		public static void OnUpdate(long clientId, CarDetailsUpdatePacket packet) => CarDetailsStore.OnUpdate((int)clientId, packet);
	}
}
