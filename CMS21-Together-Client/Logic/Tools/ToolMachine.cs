using System.Collections;
using CMS21_Together_Core.Data.GameType;

namespace CMS21Together.Logic.Tools;

public abstract class ToolMachine
{
	public abstract ModToolId Tool { get; }

	public abstract bool Present { get; }

	public abstract ToolSlotState ReadLocal();

	public abstract IEnumerator Put(ToolSlotState state);

	public abstract IEnumerator Clear();

	public virtual void ApplyFlags(ToolSlotState state) { }

	public virtual void ApplyProperty(ToolProperty property, float value) { }

	public virtual void OnClaimLost() { }

	protected static IEnumerator Step(Il2CppSystem.Collections.IEnumerator routine)
	{
		while (routine != null && routine.MoveNext()) yield return null;
	}
}
