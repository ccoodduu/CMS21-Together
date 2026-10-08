using UnityEngine;

namespace CMS21Together.UI;

public static class PointerSource
{
	public static Vector2? Override;

	public static Vector2 Position => Override ?? (Vector2)Input.mousePosition;
}
