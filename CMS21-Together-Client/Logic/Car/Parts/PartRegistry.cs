using System.Collections.Generic;
using System.Linq;
using CMS21_Together_Core.Logging;
using CMS21_Together_Core.Network.Packets;
using UnityEngine;

namespace CMS21Together.Logic.Car.Parts;

public class PartRegistry
{
	private readonly Dictionary<string, CarPart> bodyByKey = new Dictionary<string, CarPart>();
	private readonly Dictionary<int, int> bodyIndexByHandle = new Dictionary<int, int>();
	private readonly Dictionary<string, PartScript> subByKey = new Dictionary<string, PartScript>();
	private readonly Dictionary<int, int[]> subPathByInstance = new Dictionary<int, int[]>();
	private readonly Dictionary<string, int[]> subPathByKey = new Dictionary<string, int[]>();

	public int BodyCount => bodyByKey.Count;
	public int SubCount => subByKey.Count;
	public IEnumerable<string> BodyKeys => bodyByKey.Keys;
	public IEnumerable<string> SubKeys => subByKey.Keys;

	public static PartRegistry Build(CarLoader carLoader)
	{
		var registry = new PartRegistry();
		var names = new HashSet<string>();
		var body = carLoader.carParts;
		for (int i = 0; body != null && i < body.Count; i++)
		{
			var part = body[i];
			registry.bodyByKey[PartKeys.Body(i)] = part;
			if (part.handle != null) registry.bodyIndexByHandle[part.handle.GetInstanceID()] = i;
			if (!names.Add(part.name)) Log.Warn($"[Parts] {carLoader.carToLoad}: body part name '{part.name}' repeats (index {i}).");
		}

		var root = carLoader.root != null ? carLoader.root.transform : carLoader.transform;
		registry.AddSubParts(root);
		Log.Debug($"[Parts] Registry for {carLoader.carToLoad}: {registry.BodyCount} body, {registry.SubCount} mechanical, hash {registry.Hash()}.");
		return registry;
	}

	public static PartRegistry Build(Transform root)
	{
		var registry = new PartRegistry();
		registry.AddSubParts(root);
		return registry;
	}

	private void AddSubParts(Transform root)
	{
		foreach (var script in root.GetComponentsInChildren<PartScript>(true))
		{
			int[] path = PathOf(root, script.transform);
			string key = PartKeys.Sub(path);
			subByKey[key] = script;
			subPathByKey[key] = path;
			subPathByInstance[script.GetInstanceID()] = path;
		}
	}

	public CarPart Body(string key) => bodyByKey.TryGetValue(key, out var part) ? part : null;

	public PartScript Sub(string key) => subByKey.TryGetValue(key, out var script) ? script : null;

	public int[] SubPath(string key) => subPathByKey.TryGetValue(key, out var path) ? path : null;

	public bool TryGetBodyIndex(CarPart part, out int index)
	{
		index = -1;
		return part?.handle != null && bodyIndexByHandle.TryGetValue(part.handle.GetInstanceID(), out index);
	}

	public bool TryGetSubPath(PartScript script, out int[] path)
	{
		path = null;
		return script != null && subPathByInstance.TryGetValue(script.GetInstanceID(), out path);
	}

	public string Hash()
	{
		unchecked
		{
			int hash = 17;
			foreach (var key in bodyByKey.Keys.OrderBy(k => k)) hash = hash * 31 + key.GetHashCode() * 7 + (bodyByKey[key].name ?? "").GetHashCode();
			foreach (var key in subByKey.Keys.OrderBy(k => k)) hash = hash * 31 + key.GetHashCode() * 7 + (subByKey[key].id ?? "").GetHashCode();
			return hash.ToString("X8");
		}
	}

	private static int[] PathOf(Transform root, Transform target)
	{
		var indices = new List<int>();
		for (var t = target; t != null && t != root; t = t.parent) indices.Add(t.GetSiblingIndex());
		indices.Reverse();
		return indices.ToArray();
	}
}
