using System.Collections.Generic;
using CMS21_Together_Core.Network.Packets;
using UnityEngine;

namespace CMS21Together.Logic.Driving;

// remote-visual-feedback D9: states are shown 100 ms behind the newest one (Hermite with the states' velocities),
// extrapolated for at most 250 ms when the stream stops, then held. A late correction fades out instead of jumping.
public class DriveInterpolator
{
	public const float Delay = 0.1f;
	public const float MaxExtrapolation = 0.25f;
	public const float SnapDistance = 5f;
	private const int MaxStates = 32;
	private const float CorrectionHalfLife = 0.1f;
	private const float ClockSmoothing = 0.1f;

	private readonly List<DriveState> states = new List<DriveState>();
	private bool hasClock;
	private float clockOffset;
	private Vector3 correction;
	private bool wasExtrapolating;
	private bool hasShown;

	public int Snaps { get; private set; }
	public int Received { get; private set; }
	public int Late { get; private set; }
	public float BufferMs { get; private set; }
	public float RenderTime { get; private set; }
	public float ExtrapolatedMs { get; private set; }
	public float MaxExtrapolatedMs { get; private set; }
	public Vector3 Position { get; private set; }
	public Quaternion Rotation { get; private set; } = Quaternion.identity;
	public Vector3 Velocity { get; private set; }
	public DriveState Newest => states.Count == 0 ? default : states[states.Count - 1];
	public bool HasState => states.Count > 0;

	public void Push(DriveState state, float localNow)
	{
		Received++;
		if (states.Count > 0 && state.Time <= states[states.Count - 1].Time)
		{
			Late++;
			return;
		}
		float offset = localNow - state.Time;
		if (!hasClock || offset < clockOffset)
		{
			clockOffset = offset;
			hasClock = true;
		}
		else
		{
			clockOffset += (offset - clockOffset) * ClockSmoothing;
		}

		var last = states.Count == 0 ? (DriveState?)null : states[states.Count - 1];
		if (last.HasValue && Vector3.Distance(Pos(state), Pos(last.Value) + Vel(last.Value) * (state.Time - last.Value.Time)) > SnapDistance)
		{
			states.Clear();
			correction = Vector3.zero;
			hasShown = false;
			clockOffset = offset;
			Snaps++;
		}
		states.Add(state);
		if (states.Count > MaxStates) states.RemoveAt(0);
	}

	public void Hold(DriveState state)
	{
		states.Clear();
		states.Add(state);
		correction = Vector3.zero;
		Position = Pos(state);
		Rotation = Rot(state);
		Velocity = Vector3.zero;
		hasShown = true;
	}

	public bool Sample(float localNow, float deltaTime)
	{
		if (states.Count == 0) return false;
		float renderTime = localNow - clockOffset - Delay;
		RenderTime = renderTime;
		var newest = states[states.Count - 1];
		BufferMs = (newest.Time - renderTime) * 1000f;

		Vector3 position;
		Quaternion rotation;
		Vector3 velocity;
		bool extrapolating = false;
		if (renderTime <= states[0].Time || states.Count == 1 && renderTime <= newest.Time)
		{
			position = Pos(states[0]);
			rotation = Rot(states[0]);
			velocity = Vel(states[0]);
			ExtrapolatedMs = 0f;
		}
		else if (renderTime >= newest.Time)
		{
			float ahead = Mathf.Min(renderTime - newest.Time, MaxExtrapolation);
			position = Pos(newest) + Vel(newest) * ahead;
			rotation = Rot(newest);
			velocity = ahead < MaxExtrapolation ? Vel(newest) : Vector3.zero;
			extrapolating = ahead > 0f;
			ExtrapolatedMs = ahead * 1000f;
			if (ExtrapolatedMs > MaxExtrapolatedMs) MaxExtrapolatedMs = ExtrapolatedMs;
		}
		else
		{
			int i = 1;
			while (i < states.Count - 1 && states[i].Time < renderTime) i++;
			var a = states[i - 1];
			var b = states[i];
			float span = Mathf.Max(b.Time - a.Time, 1e-4f);
			float t = Mathf.Clamp01((renderTime - a.Time) / span);
			position = Hermite(Pos(a), Vel(a) * span, Pos(b), Vel(b) * span, t);
			rotation = Quaternion.Slerp(Rot(a), Rot(b), t);
			velocity = Vector3.Lerp(Vel(a), Vel(b), t);
			ExtrapolatedMs = 0f;
			while (states.Count > 2 && states[1].Time < renderTime) states.RemoveAt(0);
		}

		if (wasExtrapolating && !extrapolating && hasShown) correction = Position - position;
		wasExtrapolating = extrapolating;
		correction *= Mathf.Pow(0.5f, deltaTime / CorrectionHalfLife);
		if (correction.sqrMagnitude < 1e-6f) correction = Vector3.zero;

		Position = position + correction;
		Rotation = rotation;
		Velocity = velocity;
		hasShown = true;
		return true;
	}

	private static Vector3 Hermite(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float t)
	{
		float t2 = t * t;
		float t3 = t2 * t;
		return (2 * t3 - 3 * t2 + 1) * p0 + (t3 - 2 * t2 + t) * m0 + (-2 * t3 + 3 * t2) * p1 + (t3 - t2) * m1;
	}

	public static Vector3 Pos(DriveState s) => new Vector3(s.PosX, s.PosY, s.PosZ);
	public static Quaternion Rot(DriveState s) => new Quaternion(s.RotX, s.RotY, s.RotZ, s.RotW);
	public static Vector3 Vel(DriveState s) => new Vector3(s.VelX, s.VelY, s.VelZ);
}
