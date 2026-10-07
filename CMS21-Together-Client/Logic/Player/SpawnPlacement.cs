using CMS21_Together_Core.Data;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Network;
using UnityEngine;

namespace CMS21Together.Logic.Player;

public static class SpawnPlacement
{
	private const int SlotCount = 9;
	private const float RingRadius = 1.2f;
	private const float MinAvatarDistance = 0.8f;
	private const int BlockingLayerMask = -4194305;

	private const float FallbackGroundOffset = 0.72f;
	private const float RestoreLift = 0.05f;

	private static PlayerRestorePacket pendingRestore;

	public static void QueueRestore(PlayerRestorePacket packet, int snapshotId)
	{
		pendingRestore = packet?.Position == null || packet.Rotation == null ? null : packet;
		Log.Info(pendingRestore == null
			? "[Presence] Empty position restore ignored."
			: $"[Presence] Last garage position ({packet.Position.X:F2},{packet.Position.Y:F2},{packet.Position.Z:F2}) will be restored.");
		SyncTracker.Applied(SyncOrder.SelfKey, snapshotId);
	}

	public static void ClearRestore() => pendingRestore = null;

	public static void PlaceLocalPlayer()
	{
		if (!PresenceManager.HasLocalMotor) return;
		if (pendingRestore != null)
		{
			var restore = pendingRestore;
			pendingRestore = null;
			ApplyRestore(restore);
			return;
		}

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

	private static void ApplyRestore(PlayerRestorePacket restore)
	{
		var transform = PresenceManager.LocalMotor.transform;
		var controller = PresenceManager.LocalMotor.GetComponent<CharacterController>();
		float groundOffset = Physics.Raycast(transform.position, Vector3.down, out RaycastHit hit, 3f, BlockingLayerMask)
			? transform.position.y - hit.point.y
			: FallbackGroundOffset;
		var target = new Vector3(restore.Position.X, restore.Position.Y + groundOffset + RestoreLift, restore.Position.Z);
		float yaw = new Quaternion(restore.Rotation.X, restore.Rotation.Y, restore.Rotation.Z, restore.Rotation.W).eulerAngles.y;

		bool controllerWasEnabled = controller != null && controller.enabled;
		if (controller != null) controller.enabled = false;
		try
		{
			transform.position = target;
			transform.rotation = Quaternion.Euler(0f, yaw, 0f);
		}
		finally
		{
			if (controller != null) controller.enabled = controllerWasEnabled;
		}
		Log.Info($"[Presence] Restored the last garage position ({target.x:F2},{target.y:F2},{target.z:F2}), yaw {yaw:F0}.");
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
