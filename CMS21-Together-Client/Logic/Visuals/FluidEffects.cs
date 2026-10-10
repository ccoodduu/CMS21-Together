using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using UnityEngine;

namespace CMS21Together.Logic.Visuals;

// remote-fluid-visuals D1: a fluid effect owns a script-free copy of the receiver's own tool object. A looped sound
// is stopped before the copy is destroyed: SoundManager.LoopSFX reads every looped source each frame.
public abstract class FluidEffect : VisualEffect
{
	protected GameObject copy;
	private bool looping;

	public bool CopyAlive => copy != null && copy;
	public bool Looping => looping;
	public abstract ParticleSystem Particles { get; }

	public void DestroyCopy() => DestroyExtraGhosts();

	public override void OnEnd() => StopLoop(false);

	protected void StartLoop(string sound, float loopStart, float loopEnd)
	{
		var sounds = SoundManager.Get();
		if (sounds == null || !CopyAlive) return;
		sounds.PlayLoopSFX(copy, sound, loopStart, loopEnd);
		looping = true;
	}

	protected void StopLoop(bool playEnd)
	{
		if (!looping) return;
		looping = false;
		var sounds = SoundManager.Get();
		if (sounds != null && CopyAlive) sounds.StopLoopSFX(copy, playEnd);
	}

	protected override void DestroyExtraGhosts()
	{
		StopLoop(false);
		if (CopyAlive) Object.Destroy(copy);
		copy = null;
	}
}

// The game's drain (ToolsManager.<UseOilDrain>d__40) replayed on a copy of Oil_drain_h at the drain plug: the stream
// and the OilDrain loop while the actor drains, then the stream's tail; the plug is hidden throughout.
public sealed class DrainEffect : FluidEffect
{
	public const string PlugPath = "korek_spustowy_1(0)";
	public const string Sound = "OilDrain";
	public const float LoopStart = 0.35f;
	public const float LoopEnd = 2.55f;
	public const float StreamAlpha = 0.75f;
	public const float TailSeconds = 3f;
	public const float MaxSeconds = 60f;

	private readonly CarLoader carLoader;
	private readonly Transform plug;
	private ParticleSystem particles;
	private float tailStart;

	public override ParticleSystem Particles => particles;

	public DrainEffect(int loader, string key, int playerId, CarLoader carLoader, Transform plug)
	{
		Bind(VisualKind.Drain, loader, key, playerId);
		this.carLoader = carLoader;
		this.plug = plug;
		Phase = "stream";
	}

	public static Transform FindPlug(CarLoader carLoader)
	{
		var engine = carLoader.e_engine_h;
		if (engine == null) return null;
		var plug = engine.transform.Find(PlugPath);
		return plug != null && plug.gameObject.activeSelf ? plug : null;
	}

	public bool Build(GameObject source)
	{
		copy = FluidReplay.Copy(source, $"drain {PlayerId}");
		copy.transform.SetPositionAndRotation(plug.position, Quaternion.LookRotation(-plug.forward));
		particles = copy.GetComponentInChildren<ParticleSystem>();
		if (particles == null) return false;
		var emission = particles.emission;
		emission.enabled = true;
		float condition = carLoader.FluidsData.Oil?.Condition ?? 1f;
		var color = Color.Lerp(Color.black, Color.white, condition);
		color.a = StreamAlpha;
		var main = particles.main;
		main.startColor = new ParticleSystem.MinMaxGradient(color);
		particles.Play();
		StartLoop(Sound, LoopStart, LoopEnd);
		return true;
	}

	public void HidePlug()
	{
		foreach (var renderer in plug.GetComponentsInChildren<Renderer>(true)) VisualScope.HideRenderer(this, renderer);
	}

	public override bool Step(float dt)
	{
		if (!VisualScope.Enabled || !CopyAlive || particles == null) return false;
		if (Phase == "stream")
		{
			if (FluidReplay.WantsDrain(RemoteActivity.Of(PlayerId), Loader) && Elapsed < MaxSeconds) return true;
			particles.Stop();
			StopLoop(true);
			tailStart = Elapsed;
			Phase = "tail";
			Log.Debug($"[Visuals] Drain on loader {Loader} for player {PlayerId}: tail after {Elapsed:F1} s.");
			return true;
		}
		return particles.IsAlive(true) && Elapsed - tailStart < TailSeconds;
	}
}

// The game's refill pour (FluidRefillLogic.Update) replayed on a copy of the receiver's own pour object at the
// reservoir: the can tilts with the actor's pour power, the stream and its sound run above the game's 0.4.
public sealed class PourEffect : FluidEffect
{
	public const string PivotName = "_OilRefillPivot";
	public const float PourThreshold = 0.4f;
	public const float FullLevel = 0.65f;
	public const float TiltLow = -40f;
	public const float TiltHigh = -20f;
	public const float OilLift = 0.05f;
	public const float FollowSeconds = 0.15f;
	public const float TailSeconds = 2f;

	private readonly CarLoader carLoader;
	private readonly PartScript cap;
	private readonly ToolType toolType;
	private readonly string partKey;
	private CarFluidType fluidType;
	private Transform can;
	private Quaternion canStart;
	private ParticleSystem stream;
	private AudioSource audio;
	private float power;
	private bool streaming;
	private float tailStart;

	public override ParticleSystem Particles => stream;
	public float Power => power;
	public bool Pouring => power > PourThreshold && Phase == "pour";

	public PourEffect(int loader, int playerId, PlayerActivityState activity, CarLoader carLoader, PartScript cap)
	{
		toolType = (ToolType)activity.ToolType;
		partKey = activity.PartKey;
		Bind(VisualKind.Pour, loader, toolType.ToString(), playerId);
		this.carLoader = carLoader;
		this.cap = cap;
		Phase = "pour";
	}

	public bool Build(FluidRefill refill)
	{
		var logic = refill.fluidRefillLogic;
		fluidType = refill.carFluidType;
		var root = logic.transform;
		string canPath = FluidReplay.PathFrom(root, logic.puszka);
		string streamPath = FluidReplay.PathFrom(root, logic.emit != null ? logic.emit.transform : null);
		string fullPath = FluidReplay.PathFrom(root, logic.emitFull != null ? logic.emitFull.transform : null);
		var pose = Pose(refill, logic);

		copy = FluidReplay.Copy(logic.gameObject, $"pour {toolType} {PlayerId}");
		copy.transform.SetPositionAndRotation(pose.position, pose.rotation);
		can = FluidReplay.Find(copy, canPath);
		if (can != null) canStart = can.localRotation;
		stream = FluidReplay.Find(copy, streamPath)?.GetComponent<ParticleSystem>();
		var full = FluidReplay.Find(copy, fullPath)?.GetComponent<ParticleSystem>();
		if (full != null)
		{
			var fullEmission = full.emission;
			fullEmission.enabled = false;
		}
		audio = copy.GetComponent<AudioSource>();
		if (stream == null) return false;
		var emission = stream.emission;
		emission.enabled = false;
		stream.Play();
		return true;
	}

	public override bool Step(float dt)
	{
		if (!VisualScope.Enabled || !CopyAlive || stream == null) return false;
		if (Phase == "pour")
		{
			var activity = RemoteActivity.Of(PlayerId);
			if (FluidReplay.WantsPour(activity, Loader, (int)toolType, partKey))
			{
				power += (activity.ProgressFraction - power) * (1f - Mathf.Exp(-dt / FollowSeconds));
				Tilt(dt);
				SetStream(power > PourThreshold);
				return true;
			}
			power = 0f;
			SetStream(false);
			tailStart = Elapsed;
			Phase = "tail";
			return true;
		}
		Tilt(dt);
		return stream.IsAlive(true) && Elapsed - tailStart < TailSeconds;
	}

	private void Tilt(float dt)
	{
		if (can == null) return;
		float level = carLoader.FluidsData.GetLevel(fluidType, 0, false);
		var tilted = canStart * Quaternion.Euler(0f, 0f, level < FullLevel ? TiltLow : TiltHigh);
		can.localRotation = Quaternion.Lerp(can.localRotation, Quaternion.Lerp(canStart, tilted, power), dt * 10f);
	}

	private void SetStream(bool on)
	{
		var emission = stream.emission;
		if (streaming != on) emission.enabled = on;
		streaming = on;
		if (audio == null) return;
		if (on && !audio.isPlaying) audio.Play();
		else if (!on && audio.isPlaying) audio.Pause();
	}

	// FluidRefill.Use: the pivot beside the cap when the car has one; else oil sits 5 cm over the cap with the cap's
	// yaw, and the other cans keep their offset from the tool, which goes to the cap with the car's rotation.
	private (Vector3 position, Quaternion rotation) Pose(FluidRefill refill, FluidRefillLogic logic)
	{
		var capTransform = cap.transform;
		var pivot = capTransform.parent != null ? capTransform.parent.Find(PivotName) : null;
		if (pivot != null) return (pivot.position, pivot.rotation);
		if (fluidType == CarFluidType.EngineOil)
			return (capTransform.position + Vector3.up * OilLift, Quaternion.Euler(0f, capTransform.localEulerAngles.y, 0f));
		var carRoot = carLoader.root != null ? carLoader.root.transform : carLoader.transform;
		if (!logic.transform.IsChildOf(refill.transform)) return (capTransform.position, carRoot.rotation);
		var toolRotation = carRoot.rotation;
		var offset = refill.transform.InverseTransformPoint(logic.transform.position);
		var relative = Quaternion.Inverse(refill.transform.rotation) * logic.transform.rotation;
		return (capTransform.position + toolRotation * Vector3.Scale(offset, refill.transform.lossyScale), toolRotation * relative);
	}
}
