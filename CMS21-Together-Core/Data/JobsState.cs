using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using CMS21_Together_Core.Data.GameType;
using Newtonsoft.Json;

namespace CMS21_Together_Core.Data;

public enum OrderStatus
{
	Open,
	Claimed
}

[Serializable]
public class OrderEntry
{
	public ModJob Job;
	public float RemainingSeconds;
	public OrderStatus Status;
	[JsonIgnore] public long ClaimedBy;
	[JsonIgnore] public float ClaimedAt;
}

[Serializable]
public class ActiveJobEntry
{
	public ModJob Job;
	public int CarLoaderId = -1;
	public float OriginalSeconds;
	[OptionalField] public ModJob OrderJob;
	[OptionalField] public List<string> Contributors = new List<string>();
}

[Serializable]
public class JobsState
{
	public List<OrderEntry> Orders = new List<OrderEntry>();
	public List<ActiveJobEntry> ActiveJobs = new List<ActiveJobEntry>();
	public int NextJobId = 1;
	public ModMissionState Missions = new ModMissionState();
	[OptionalField] public OrderClock Clock = new OrderClock();
}

[Serializable]
public class OrderClock
{
	public float OrderTimer;
	public float NextOrderTime = 10f;
}
