using CMS21_Together_Core.Logging;
using CMS21Together.Network;
using UnityEngine;

namespace CMS21Together.Logic.Player;

public static class SpawnPlacement
{
	private const int SlotCount = 9;
	private const float RingRadius = 1.2f;
	private const float MinAvatarDistance = 0.8f;
	private const int BlockingLayerMask = -4194305;

	public static bool RestoreApplied { get; set; }

	public static void PlaceLocalPlayer()
	{
		if (RestoreApplied || !PresenceManager.HasLocalMotor) return;

		var transform = PresenceManager.LocalMotor.transform;
		var controller = PresenceManager.LocalMotor.GetComponent<CharacterController>();
		Vector3 spawn = transform.position;
		Quaternion frame = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

		bool controllerWasEnabled = controller != null && controller.enabled;
		if (controller != null) controller.enabled = false;
		try
		{
			int start = (Client.Instance.ID - 1) % SlotCount;
			for (int i = 0; i < SlotCount; i++)
			{
				int slot = (start + i) % SlotCount;
				Vector3 candidate = SlotPosition(spawn, frame, slot);
				if (!IsFree(candidate, controller)) continue;

				transform.position = candidate;
				Log.Info($"[Presence] spawn slot {slot} at ({candidate.x:F2},{candidate.y:F2},{candidate.z:F2})");
				return;
			}
			Log.Warn("[Presence] No free spawn slot, staying at the vanilla spawn.");
		}
		finally
		{
			if (controller != null) controller.enabled = controllerWasEnabled;
		}
	}

	private static Vector3 SlotPosition(Vector3 spawn, Quaternion frame, int slot)
	{
		if (slot == 0) return spawn;
		float angle = (slot - 1) * 45f * (float)(System.Math.PI / 180.0);
		return spawn + frame * new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * RingRadius;
	}

	private static bool IsFree(Vector3 position, CharacterController controller)
	{
		foreach (var player in PresenceManager.Roster.Values)
		{
			if (!player.HasAvatar) continue;
			Vector3 other = player.Avatar.transform.position;
			if (new Vector2(other.x - position.x, other.z - position.z).magnitude < MinAvatarDistance) return false;
		}

		if (controller == null) return true;
		float radius = controller.radius;
		Vector3 center = position + controller.center;
		float half = Mathf.Max(0f, controller.height / 2f - radius);
		Vector3 bottom = center - Vector3.up * half;
		Vector3 top = center + Vector3.up * half;
		return !Physics.CheckCapsule(bottom, top, radius, BlockingLayerMask, QueryTriggerInteraction.Ignore);
	}
}
