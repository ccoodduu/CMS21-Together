using System;
using System.Collections.Generic;
using CMS21_Together_Core.Logging;
using UnhollowerBaseLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CMS21Together.Logic.Visuals;

// remote-visual-feedback D1: a ghost is new GameObjects with MeshFilter/MeshRenderer copies of the source renderers
// (shared mesh, copies of the shared materials, the source's layer), no colliders and no scripts, at the scene root.
public class Ghost
{
	public const string Dissolve = "dissolve";
	public const string Shrink = "shrink";

	private static int dissolveId = -1;
	private static bool dissolveIdKnown;

	private readonly List<Material> copies = new List<Material>();
	private readonly bool hasDissolve;
	private float alpha = 1f;
	private float scale = 1f;

	public GameObject Root { get; private set; }
	public int Pieces { get; private set; }
	public string FadeMode => hasDissolve ? Dissolve : Shrink;

	private Ghost(GameObject root, bool hasDissolve, int pieces)
	{
		Root = root;
		this.hasDissolve = hasDissolve;
		Pieces = pieces;
	}

	public bool IsAlive => Root != null && Root;

	public static Ghost Create(string name, IEnumerable<Renderer> sources, Vector3 position, Quaternion rotation, out string skipReason, int layer = -1)
	{
		skipReason = null;
		var root = new GameObject($"TogetherGhost[{name}]");
		root.transform.SetPositionAndRotation(position, rotation);
		var copies = new List<Material>();
		int pieces = 0, skinned = 0;
		bool dissolve = false;
		int id = DissolveId();

		foreach (var source in sources)
		{
			if (source == null || !source) continue;
			var meshRenderer = source.TryCast<MeshRenderer>();
			var filter = meshRenderer == null ? null : source.GetComponent<MeshFilter>();
			if (filter == null || filter.sharedMesh == null)
			{
				skinned++;
				continue;
			}

			var piece = new GameObject(source.name);
			piece.layer = layer >= 0 ? layer : source.gameObject.layer;
			piece.transform.SetParent(root.transform, false);
			piece.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
			piece.transform.localScale = source.transform.lossyScale;
			piece.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
			var renderer = piece.AddComponent<MeshRenderer>();
			var shared = source.sharedMaterials;
			var materials = new Il2CppReferenceArray<Material>(shared.Length);
			for (int i = 0; i < shared.Length; i++)
			{
				if (shared[i] == null) continue;
				var copy = new Material(shared[i]);
				copies.Add(copy);
				materials[i] = copy;
				if (id >= 0 && copy.HasProperty(id)) dissolve = true;
			}
			renderer.sharedMaterials = materials;
			renderer.shadowCastingMode = meshRenderer.shadowCastingMode;
			pieces++;
		}

		if (pieces == 0)
		{
			Object.Destroy(root);
			foreach (var copy in copies) Object.Destroy(copy);
			skipReason = skinned > 0 ? "noMeshRenderer" : "noRenderers";
			return null;
		}
		var ghost = new Ghost(root, dissolve, pieces);
		ghost.copies.AddRange(copies);
		return ghost;
	}

	public void SetPose(Vector3 position, Quaternion rotation)
	{
		if (!IsAlive) return;
		Root.transform.SetPositionAndRotation(position, rotation);
	}

	public void SetScale(float value)
	{
		scale = value;
		ApplyScale();
	}

	public void SetAlpha(float value)
	{
		alpha = Mathf.Clamp01(value);
		if (!IsAlive) return;
		if (hasDissolve)
		{
			foreach (var copy in copies)
				if (copy != null && copy.HasProperty(dissolveId)) copy.SetFloat(dissolveId, alpha);
			return;
		}
		ApplyScale();
	}

	public void Destroy()
	{
		if (IsAlive) Object.Destroy(Root);
		Root = null;
		foreach (var copy in copies)
			if (copy != null) Object.Destroy(copy);
		copies.Clear();
	}

	private void ApplyScale()
	{
		if (!IsAlive) return;
		float shrink = hasDissolve ? 1f : Mathf.Clamp01(alpha * 2f);
		Root.transform.localScale = Vector3.one * (scale * shrink);
	}

	private static int DissolveId()
	{
		if (dissolveIdKnown) return dissolveId;
		dissolveIdKnown = true;
		try
		{
			dissolveId = CMS.ShaderProperties.AlphaDissolve;
		}
		catch (Exception e)
		{
			dissolveId = -1;
			Log.Warn($"[Visuals] ShaderProperties.AlphaDissolve is not readable, ghosts shrink instead: {e.Message}");
		}
		return dissolveId;
	}
}
