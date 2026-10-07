using System.Collections.Generic;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Logic.Car.Parts;
using UnityEngine;

namespace CMS21Together.Logic.Visuals;

public enum VisualKind
{
	Off,
	On,
	Panel,
	Swing,
	Bolts
}

// A visual owns only what it creates (ghosts, material copies) plus forceRenderingOff on real renderers, which
// VisualScope records and restores when the effect ends for any reason.
public abstract class VisualEffect
{
	public VisualKind Kind { get; protected set; }
	public int Loader { get; protected set; } = PlayerActivityState.NoCar;
	public string Key { get; protected set; }
	public int PlayerId { get; protected set; } = -1;
	public string Phase { get; protected set; } = "start";
	public string Fade => ghost?.FadeMode;
	public float Elapsed { get; internal set; }
	public bool Ended { get; internal set; }

	internal readonly List<int> Hidden = new List<int>();
	internal int SpawnSeq;

	protected Ghost ghost;

	public virtual float HoldPoint => float.MaxValue;

	public abstract bool Step(float dt);

	public virtual void OnEnd() { }

	protected void Bind(VisualKind kind, int loader, string key, int playerId)
	{
		Kind = kind;
		Loader = loader;
		Key = key;
		PlayerId = playerId;
		SpawnSeq = CarPartsSync.SpawnSeq(loader);
	}

	internal void DestroyGhosts()
	{
		ghost?.Destroy();
		ghost = null;
		DestroyExtraGhosts();
	}

	protected virtual void DestroyExtraGhosts() { }

	protected static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

	protected static float EaseInOut(float t) => t * t * (3f - 2f * t);
}
