using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using CMS21_Together_Core.Network;

namespace CMS21_Together_Core.Data.Compatibility;

public static class ProtocolHash
{
	private static string value;

	public static string Value => value ??= Compute();

	public static string Compute()
	{
		var core = typeof(ProtocolHash).Assembly;
		var descriptions = new SortedDictionary<string, string>(StringComparer.Ordinal);

		var packetTypes = System.Enum.GetValues(typeof(PacketTypes)).Cast<PacketTypes>()
			.Select(p => $"{p}={(int)p}").OrderBy(s => s, StringComparer.Ordinal);
		descriptions["#PacketTypes"] = string.Join(";", packetTypes);

		foreach (var type in core.GetTypes().Where(t => t.GetCustomAttribute<NetworkPacket>() != null))
			Describe(type, core, descriptions);

		var text = string.Join("\n", descriptions.Select(d => $"{d.Key}:{d.Value}"));
		using var sha = SHA256.Create();
		var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
		return BitConverter.ToString(hash).Replace("-", "").Substring(0, 16).ToLowerInvariant();
	}

	private static void Describe(Type type, Assembly core, SortedDictionary<string, string> descriptions)
	{
		string key = TypeName(type);
		if (descriptions.ContainsKey(key)) return;

		if (type.IsEnum)
		{
			descriptions[key] = string.Join(";", System.Enum.GetNames(type).Select(n => $"{n}={Convert.ToInt64(System.Enum.Parse(type, n))}"));
			return;
		}

		var fields = SerializableFields(type).ToList();
		descriptions[key] = string.Join(";", fields
			.Select(f => $"{f.DeclaringType?.Name}.{f.Name}:{TypeName(f.FieldType)}{(f.IsDefined(typeof(OptionalFieldAttribute)) ? "?" : "")}")
			.OrderBy(s => s, StringComparer.Ordinal));

		foreach (var field in fields)
		foreach (var referenced in ReferencedTypes(field.FieldType))
			if (referenced.Assembly == core) Describe(referenced, core, descriptions);
	}

	private static IEnumerable<FieldInfo> SerializableFields(Type type)
	{
		for (var t = type; t != null && t != typeof(object); t = t.BaseType)
			foreach (var field in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
				if (!field.IsNotSerialized) yield return field;
	}

	private static IEnumerable<Type> ReferencedTypes(Type type)
	{
		yield return type;
		if (type.HasElementType)
			foreach (var inner in ReferencedTypes(type.GetElementType())) yield return inner;
		if (type.IsGenericType)
			foreach (var argument in type.GetGenericArguments())
			foreach (var inner in ReferencedTypes(argument)) yield return inner;
	}

	private static string TypeName(Type type)
	{
		if (type.IsArray) return TypeName(type.GetElementType()) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
		if (type.IsGenericType && !type.IsGenericTypeDefinition)
			return type.GetGenericTypeDefinition().FullName + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">";
		return type.FullName ?? type.Name;
	}
}
