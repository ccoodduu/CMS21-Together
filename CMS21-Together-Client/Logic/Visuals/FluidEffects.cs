using CMS21_Together_Core.Logging;
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
