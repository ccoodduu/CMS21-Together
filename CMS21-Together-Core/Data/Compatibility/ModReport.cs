using System;
using System.Collections.Generic;

namespace CMS21_Together_Core.Data.Compatibility;

[Serializable]
public class ModReport
{
	public string Name;
	public string Version;
	public string Author;
	public string File;
	public string Assembly;
	public List<PatchTarget> Targets = new List<PatchTarget>();

	public override string ToString() => string.IsNullOrEmpty(Version) ? Name : $"{Name} {Version}";
}

[Serializable]
public class PatchTarget
{
	public string Assembly;
	public string Type;
	public string Method;

	public PatchTarget() { }

	public PatchTarget(string assembly, string type, string method)
	{
		Assembly = assembly;
		Type = type;
		Method = method;
	}

	public override string ToString() => $"{SimpleTypeName}.{Method}";

	public string SimpleTypeName
	{
		get
		{
			string type = Type ?? "";
			int dot = type.LastIndexOf('.');
			return dot >= 0 ? type.Substring(dot + 1) : type;
		}
	}
}
