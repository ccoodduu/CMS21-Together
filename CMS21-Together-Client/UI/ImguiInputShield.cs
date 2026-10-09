using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CMS21Together.UI;

public static class ImguiInputShield
{
	private static EventSystem disabled;

	public static void Update()
	{
		var pointer = PointerSource.Position;
		var guiPoint = new Vector2(pointer.x, Screen.height - pointer.y);
		bool overPanel = ImguiView.Covered().Any(rect => rect.Contains(guiPoint));
		if (overPanel && disabled == null)
		{
			var events = EventSystem.current;
			if (events == null) return;
			events.enabled = false;
			disabled = events;
		}
		else if (!overPanel && disabled != null)
		{
			disabled.enabled = true;
			disabled = null;
		}
	}
}
