using System.Collections.Generic;
using CMS21_Together_Core.Logging;
using UnityEngine;

namespace CMS21Together.Logic.Driving;

// track-races D7: the race track's painted start grid (spike docs/spikes/race-grid.md): 10 rows of 2 boxes behind the
// line, the game's CarSpawnPosition on the front left box. A racer's client moves that spawn onto its box before the
// game's own restart, which then puts the car there, and moves it back once the restart is done.
public static class RaceGrid
{
	public const int Boxes = 20;
	public const float ColumnSpacing = 6.15f;
	public const float RowSpacing = 10f;
	private const float RayHeight = 3f;
	private const float RayLength = 10f;

	private static Transform movedSpawn;
	private static Vector3 originalPosition;

	public static bool Moved => movedSpawn != null;
	public static int LastBox { get; private set; } = -1;
	public static Vector3 LastSpawnPosition { get; private set; }

	public static int BoxOfIndex(int index) => index < 0 ? -1 : index < Boxes ? index : Boxes - 1 - (index - Boxes) % Boxes;

	public static int BoxOf(List<int> grid, int playerId) => grid == null ? 0 : BoxOfIndex(grid.IndexOf(playerId));

	public static Vector3 Offset(Transform spawn, int box) =>
		spawn.right * (ColumnSpacing * (box % 2)) - spawn.forward * (RowSpacing * (box / 2));

	public static bool MoveSpawn(int raceId, int box)
	{
		Restore("a new race");
		var spawn = TrackManager.Instance?.GetCarPhysics()?.carSpawnPosition;
		if (spawn == null)
		{
			Log.Warn($"[Race] Race {raceId}: no car spawn position, so the car starts on the game's own spot.");
			return false;
		}
		LastBox = box;
		originalPosition = spawn.position;
		if (box <= 0)
		{
			LastSpawnPosition = originalPosition;
			return true;
		}
		var target = originalPosition + Offset(spawn, box);
		float ground = GroundBelow(target), originalGround = GroundBelow(originalPosition);
		if (!float.IsNaN(ground) && !float.IsNaN(originalGround)) target.y += ground - originalGround;
		spawn.position = target;
		movedSpawn = spawn;
		LastSpawnPosition = target;
		Log.Info($"[Race] Race {raceId}: start on grid box {box + 1} (row {box / 2 + 1}, {(box % 2 == 0 ? "left" : "right")}).");
		return true;
	}

	public static void Restore(string why)
	{
		if (movedSpawn == null)
		{
			// Also true for a spawn destroyed with its scene; drop the stale wrapper.
			movedSpawn = null;
			return;
		}
		movedSpawn.position = originalPosition;
		movedSpawn = null;
		Log.Info($"[Race] Car spawn back on the game's own spot ({why}).");
	}

	private static float GroundBelow(Vector3 point)
	{
		float best = float.NaN;
		foreach (var hit in Physics.RaycastAll(point + Vector3.up * RayHeight, Vector3.down, RayLength, ~0, QueryTriggerInteraction.Ignore))
			if (hit.collider != null && hit.collider.attachedRigidbody == null && (float.IsNaN(best) || hit.point.y > best)) best = hit.point.y;
		return best;
	}
}
