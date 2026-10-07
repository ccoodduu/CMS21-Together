using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using CMS21_Together_Core.Data.Digest;
using Newtonsoft.Json;

namespace CMS21_Together_Core.Diagnostics;

public static class BugReportId
{
	private static readonly Regex Pattern = new Regex(@"^\d{8}-\d{6}-[0-9a-f]{4}$");

	public static string New(DateTime utc) =>
		$"{utc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{Guid.NewGuid().ToString("N").Substring(0, 4)}";

	public static bool IsValid(string id) => id != null && Pattern.IsMatch(id);
}

public class BugReportBundle
{
	public const int NewestLogs = 5;

	private readonly string root;
	private readonly List<string> secrets;
	private readonly List<KeyValuePair<string, byte[]>> entries = new List<KeyValuePair<string, byte[]>>();

	public List<string> Errors { get; } = new List<string>();

	public BugReportBundle(string root, IEnumerable<string> secrets)
	{
		this.root = root;
		this.secrets = secrets?.Where(s => !string.IsNullOrEmpty(s)).ToList() ?? new List<string>();
	}

	public IEnumerable<string> Names => entries.Select(e => e.Key);

	public void AddText(string name, string text) =>
		entries.Add(new KeyValuePair<string, byte[]>(EntryName(name), new UTF8Encoding(false).GetBytes(Redaction.Scrub(text ?? "", secrets))));

	public void AddJson(string name, object value) => AddText(name, JsonConvert.SerializeObject(value, Formatting.Indented));

	public void AddState(IEnumerable<(string Key, string SubKey, Projection Projection)> sections)
	{
		foreach (var section in sections.GroupBy(s => s.Key))
		{
			var list = section.ToList();
			if (list.Count == 1 && string.IsNullOrEmpty(list[0].SubKey)) AddJson($"state/{section.Key}.json", StateEntry(list[0].Projection));
			else AddJson($"state/{section.Key}.json", list.ToDictionary(s => s.SubKey ?? "", s => StateEntry(s.Projection)));
		}
	}

	private static object StateEntry(Projection projection) => projection == null
		? (object)"not ready"
		: new { hash = projection.Hash().ToString("X16"), rows = projection.Sorted().Select(r => $"{r.Id}|{r.Field}={r.Value}").ToList() };

	public void AddFile(string name, string path, Func<string, string> transform = null)
	{
		try
		{
			string text = ReadShared(path);
			AddText(name, transform == null ? text : transform(text));
		}
		catch (Exception e)
		{
			Errors.Add($"{name}: {e.Message}");
		}
	}

	public void AddNewest(string folder, string directory, string pattern, int count)
	{
		try
		{
			if (!Directory.Exists(directory)) return;
			foreach (var file in new DirectoryInfo(directory).GetFiles(pattern).OrderByDescending(f => f.LastWriteTimeUtc).Take(count))
				AddFile($"{folder}/{file.Name}", file.FullName);
		}
		catch (Exception e)
		{
			Errors.Add($"{folder}: {e.Message}");
		}
	}

	public long Write(string path)
	{
		if (Errors.Count > 0) AddText("errors.txt", string.Join("\r\n", Errors));
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		string temp = path + ".tmp";
		using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
		using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
		{
			foreach (var entry in entries)
			{
				using (var output = zip.CreateEntry(entry.Key, CompressionLevel.Optimal).Open())
					output.Write(entry.Value, 0, entry.Value.Length);
			}
		}
		if (File.Exists(path)) File.Delete(path);
		File.Move(temp, path);
		return new FileInfo(path).Length;
	}

	public static string ReadShared(string path)
	{
		using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
		using (var reader = new StreamReader(stream, Encoding.UTF8, true))
			return reader.ReadToEnd();
	}

	public static List<string> FileList(string baseDirectory, params string[] folders)
	{
		var lines = new List<string>();
		foreach (string folder in folders)
		{
			string directory = Path.Combine(baseDirectory, folder);
			if (!Directory.Exists(directory))
			{
				lines.Add($"{folder}\\ (missing)");
				continue;
			}
			foreach (var file in new DirectoryInfo(directory).GetFiles("*", SearchOption.AllDirectories).OrderBy(f => f.FullName, StringComparer.OrdinalIgnoreCase))
				lines.Add($"{file.FullName.Substring(baseDirectory.TrimEnd('\\', '/').Length + 1)}\t{file.Length}\t{file.LastWriteTimeUtc:yyyy-MM-dd HH:mm:ss}");
		}
		return lines;
	}

	private string EntryName(string name) => $"{root}/{name.Replace('\\', '/')}";
}
