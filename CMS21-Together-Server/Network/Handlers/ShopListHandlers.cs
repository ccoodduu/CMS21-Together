using CMS21_Together_Core;
using CMS21_Together_Core.Network;
using CMS21_Together_Core.Network.Packets;
using CMS21_Together_Server.Data.ShopList;

namespace CMS21_Together_Server.Network.Handlers
{
	public static class ShopListHandlers
	{
		[PacketHandler(PacketTypes.ShopListChange)]
		public static void OnChange(long clientId, ShopListChangePacket packet) => ShopListService.OnChange((int)clientId, packet);
	}
}
