using CMS21_Together_Core.Data.Enum;
using CMS21_Together_Core.Data.GameType;
using CMS21_Together_Core.Network.Packets;
using CMS21Together.Data;
using CMS21Together.Network;
using HarmonyLib;
using UnityEngine;
using System.Reflection;

namespace CMS21Together.Logic.Player;

public static class Movement
{
	private const float SendRate = 0.05f; // Increased to 20Hz
	private const float MinMoveDistanceSqr = 0.01f * 0.01f;
	private const float MinRotAngle = 1.0f;
	private const int GroundLayerMask = -4194305;

	private static Vector3 lastSentPosition;
	private static Quaternion lastSentRotation;
	private static Vector3 lastSentVelocity;
	private static float lastSentPitch;
	private static bool lastSentCrouching;
	private static bool lastSentRunning;
	private static float nextSendTime;
	private static bool forceSend;

	private static System.Func<bool> GetIsCrouching;

	public static void ForceSend()
	{
		forceSend = true;
		nextSendTime = 0f;
	}

	public static void UpdateMovement()
	{
		if (ClientScene.LocalScene == GameScene.Loading || !PresenceManager.HasLocalMotor) return;
		if (Time.time < nextSendTime) return;

		var packet = CaptureLocal();
		var currentPos = new Vector3(packet.Position.X, packet.Position.Y, packet.Position.Z);
		var currentRot = new Quaternion(packet.Rotation.X, packet.Rotation.Y, packet.Rotation.Z, packet.Rotation.W);
		var velocity = new Vector3(packet.Velocity.X, packet.Velocity.Y, packet.Velocity.Z);

		bool changed = forceSend
		               || (currentPos - lastSentPosition).sqrMagnitude > MinMoveDistanceSqr
		               || Quaternion.Angle(currentRot, lastSentRotation) > MinRotAngle
		               || (velocity - lastSentVelocity).sqrMagnitude > 0.01f
		               || packet.IsCrouching != lastSentCrouching
		               || packet.IsRunning != lastSentRunning
		               || Mathf.Abs(Mathf.DeltaAngle(packet.CameraPitch, lastSentPitch)) > MinRotAngle;
		if (!changed) return;

		Client.Instance.Send(packet);

		forceSend = false;
		lastSentPosition = currentPos;
		lastSentRotation = currentRot;
		lastSentVelocity = velocity;
		lastSentPitch = packet.CameraPitch;
		lastSentCrouching = packet.IsCrouching;
		lastSentRunning = packet.IsRunning;
		nextSendTime = Time.time + SendRate;
	}

	public static MovementPacket CaptureLocal()
	{
		var motor = PresenceManager.LocalMotor;
		var transform = motor.transform;
		Vector3 position = transform.position;
		if (Physics.Raycast(position, Vector3.down, out RaycastHit hit, 3f, GroundLayerMask))
			position.y = hit.point.y;
		else
			position.y -= 0.72f;

		Quaternion rotation = transform.rotation;
		Vector3 velocity = motor.movement.velocity;
		bool isRunning = Singleton<GameManager>.Instance.InputManager.GameplayRun() && velocity.sqrMagnitude > 0.1f;

		return new MovementPacket
		{
			SenderId = Client.Instance.ID,
			Scene = ClientScene.LocalScene,
			Position = new Vector3Serializable(position.x, position.y, position.z),
			Velocity = new Vector3Serializable(velocity.x, velocity.y, velocity.z),
			Rotation = new QuaternionSerializable(rotation.x, rotation.y, rotation.z, rotation.w),
			CameraPitch = Camera.main != null ? Camera.main.transform.eulerAngles.x : 0f,
			IsGrounded = motor.grounded,
			IsCrouching = IsCrouching(),
			IsRunning = isRunning
		};
	}

	private static bool IsCrouching()
	{
		if (GetIsCrouching == null)
		{
			PropertyInfo property = AccessTools.Property(typeof(FPSCamera), "isCrouching");
			FieldInfo field = property == null ? AccessTools.Field(typeof(FPSCamera), "isCrouching") : null;
			if (property != null) GetIsCrouching = () => (bool)property.GetValue(null);
			else if (field != null) GetIsCrouching = () => (bool)field.GetValue(null);
			else GetIsCrouching = () => false;
		}
		return GetIsCrouching();
	}
}
