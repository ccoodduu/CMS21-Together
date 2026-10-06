using System.Collections.Generic;
using CMS21_Together_Core.Logging;
using UnityEngine;

namespace CMS21Together.UI;

public static class ModNotify
{
	private const float ToastSeconds = 5f;
	private const int HistorySize = 10;

	public class Toast
	{
		public string Text;
		public float ShownAt;
	}

	public static readonly List<Toast> History = new List<Toast>();
	public static readonly Queue<(string Title, string Text)> Messages = new Queue<(string, string)>();

	public static void ShowToast(string text)
	{
		History.Add(new Toast { Text = text, ShownAt = Time.realtimeSinceStartup });
		if (History.Count > HistorySize) History.RemoveAt(0);
		Log.Info($"[Notify] {text}");
	}

	public static void ShowMessage(string title, string text)
	{
		Messages.Enqueue((title, text));
		Log.Info($"[Notify] {title}: {text}");
	}

	public static IEnumerable<Toast> Visible()
	{
		float now = Time.realtimeSinceStartup;
		foreach (var toast in History)
			if (now - toast.ShownAt < ToastSeconds) yield return toast;
	}
}
