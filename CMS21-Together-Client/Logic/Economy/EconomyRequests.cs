using System;
using System.Collections.Generic;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Network;
using CMS21Together.UI;

namespace CMS21Together.Logic.Economy;

public static class EconomyRequests
{
	private const int RecentKept = 20;

	private static readonly Dictionary<int, Action<EconomyResultPacket>> pending = new Dictionary<int, Action<EconomyResultPacket>>();
	private static int nextId = 1;

	public static readonly Queue<EconomyRequestPacket> Recent = new Queue<EconomyRequestPacket>();
	public static EconomyRequestPacket Last { get; private set; }
	public static EconomyResultPacket LastResult { get; private set; }

	public static int Send(EconomyRequestPacket request, Action<EconomyResultPacket> onResult = null)
	{
		request.RequestId = nextId++;
		if (onResult != null) pending[request.RequestId] = onResult;
		Last = request;
		Recent.Enqueue(request);
		while (Recent.Count > RecentKept) Recent.Dequeue();
		EconomyAudit.Count(EconomyAudit.Sent, request.Reason.ToString());
		Log.Info($"[Economy] Request {request.RequestId}: {request.Reason}({request.Arg}) money {request.Money}, scraps {request.Scraps}, exp {request.Exp}{(request.ItemUid != 0 ? $", item {request.ItemUid}" : "")}{(request.CarLoaderId >= 0 ? $", loader {request.CarLoaderId}" : "")}.");
		Client.Instance.Send(request);
		return request.RequestId;
	}

	public static void SendFee(EconomyScopeEntry scope, int money, int scraps, int exp)
	{
		int arg = scope.Reason == EconomyReason.Tint ? -money / 50 : scope.Arg;
		Send(new EconomyRequestPacket
		{
			Reason = scope.Reason, Money = money, Scraps = scraps, Exp = exp, Arg = arg, Arg2 = scope.Arg2,
			ItemUid = scope.ItemUid, CarLoaderId = scope.Loader,
		});
	}

	public static void SendWork(int exp) => Send(new EconomyRequestPacket { Reason = EconomyReason.Work, Exp = exp });

	public static bool Resend()
	{
		if (Last == null) return false;
		Log.Info($"[Economy] Resending request {Last.RequestId} ({Last.Reason}).");
		Client.Instance.Send(Last);
		return true;
	}

	public static void OnResult(EconomyResultPacket result)
	{
		LastResult = result;
		if (result.RequestId == 0)
		{
			if (result.Reason == EconomyReason.SkillReset) ModNotify.ShowToast("Another player reset the skills.");
			return;
		}
		if (!result.Accepted) EconomyAudit.Count(EconomyAudit.Refused, result.Reason.ToString());
		Log.Info($"[Economy] Result {result.RequestId}: {result.Reason} {(result.Accepted ? "accepted" : $"refused {result.Refusal}")}, money {result.Money}, scraps {result.Scraps}.");
		if (!pending.TryGetValue(result.RequestId, out var onResult)) return;
		pending.Remove(result.RequestId);
		try
		{
			onResult(result);
		}
		catch (Exception ex)
		{
			Log.Error($"[Economy] Handling the result of {result.Reason} failed: {ex.Message}");
		}
	}

	public static string RefusalText(EconomyRefusal refusal) => refusal switch
	{
		EconomyRefusal.NoMoney => "Not enough money.",
		EconomyRefusal.NoScraps => "Not enough scrap.",
		EconomyRefusal.Busy => "Another player is using it right now.",
		EconomyRefusal.Gone => "It is no longer there.",
		_ => "The server refused this.",
	};

	public static void Reset()
	{
		pending.Clear();
		Recent.Clear();
		Last = null;
		LastResult = null;
	}
}
