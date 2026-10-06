using System;
using System.Collections.Generic;
using CMS21Together.Logic.Hook;
using CMS21Together.Network.Handlers;

namespace CMS21Together.Logic.Car.Parts;

public static class ApplyingRemote
{
	private static readonly HashSet<int> active = new HashSet<int>();

	public static bool IsActive(int loader) => active.Contains(loader);

	public static IDisposable Scope(int loader) => new ScopeHandle(loader);

	private sealed class ScopeHandle : IDisposable
	{
		private readonly int loader;
		private readonly bool previousIgnore;

		public ScopeHandle(int loader)
		{
			this.loader = loader;
			active.Add(loader);
			CarSpawnHooks.Suppress(loader);
			previousIgnore = InventoryHandlers.IgnoreInventoryHooks;
			InventoryHandlers.IgnoreInventoryHooks = true;
		}

		public void Dispose()
		{
			active.Remove(loader);
			CarSpawnHooks.Release(loader);
			InventoryHandlers.IgnoreInventoryHooks = previousIgnore;
		}
	}
}
