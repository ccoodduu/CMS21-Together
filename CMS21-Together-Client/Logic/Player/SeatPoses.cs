using CMS21_Together_Core.Data;
using CMS21_Together_Core.Data.Enum;
using CMS21Together.Data;
using CMS21Together.Logic.Car.Parts;
using CMS21Together.Logic.Driving;
using UnityEngine;

namespace CMS21Together.Logic.Player;

public static class SeatPoses
{
	public const float SeatedAvatarDrop = 0.45f;

	public static bool IsSeatedInGarage(PlayerPresenceRecord record)
	{
		return record != null && record.Scene == GameScene.Garage && record.SeatCarLoaderId != PlayerPresenceRecord.NoCar;
	}

	public static bool TryGetHandle(CarLoader carLoader, bool leftHandle, out Vector3 handlePosition, out Quaternion frame)
	{
		handlePosition = Vector3.zero;
		frame = Quaternion.identity;
		if (carLoader == null || !carLoader || !RideAlong.CarFrame(carLoader, out frame, out _)) return false;
		var handle = leftHandle ? carLoader.GetLeftSeatHandle() : carLoader.GetRightSeatHandle();
		if (handle == null || !handle) return false;
		handlePosition = handle.transform.position;
		return true;
	}

	public static bool TryGet(CarLoader carLoader, bool leftHandle, out Vector3 position, out Quaternion rotation)
	{
		if (!TryGetHandle(carLoader, leftHandle, out position, out rotation)) return false;
		position -= rotation * Vector3.up * SeatedAvatarDrop;
		return true;
	}

	public static bool TryGetGarage(PlayerPresenceRecord record, out Vector3 position, out Quaternion rotation)
	{
		position = Vector3.zero;
		rotation = Quaternion.identity;
		var carLoader = SeatedCar(record);
		return carLoader != null && CarPartsSync.IsReady(record.SeatCarLoaderId) && TryGet(carLoader, record.SeatLeft, out position, out rotation);
	}

	public static bool IsPosed(PlayerPresenceRecord record) => TryGetGarage(record, out _, out _);

	public static void Place(PlayerInstance avatar, Vector3 position, Quaternion rotation)
	{
		if (!avatar.gameObject.activeSelf) avatar.gameObject.SetActive(true);
		avatar.UpdateNetworkState(position, rotation, Vector3.zero, 0f, true, true, false);
		avatar.transform.SetPositionAndRotation(position, rotation);
	}

	private static CarLoader SeatedCar(PlayerPresenceRecord record)
	{
		if (!IsSeatedInGarage(record) || !ClientScene.IsGarageReady) return null;
		var carLoader = CarLoaderPlaces.Get()?.GetCarLoaderByIndex(record.SeatCarLoaderId);
		return carLoader == null || string.IsNullOrEmpty(carLoader.carToLoad) ? null : carLoader;
	}
}
