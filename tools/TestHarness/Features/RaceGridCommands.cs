using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MelonLoader;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TogetherTestHarness.Features;

// race-grid spike: looks for marked start spots on the race track. "grid-start" reads the game's start spot (the
// scene's CarSpawnPosition; StartPosition is null there) and the checkpoints, "grid-find" lists scene objects by
// name/mesh/material or by distance from the start spot (positions also relative to it: right, up, forward),
// "grid-shot" renders a camera of our own into a texture and saves it as PNG, so the game window can stay minimized and
// unfocused, and "grid-spawn-move" moves the start spot so the game's own restart puts the car on another grid box.
public static class RaceGridCommands
{
    private const string DefaultPattern = "(?i)start|grid|pole|spawn|finish|slot|marker|mark|decal|paint|place|position|line|box";
    private static string lastShot;
    private static string lastShotError;

    private static float Round(float v) => (float)Math.Round(v, 2);
    private static object Vec(Vector3 v) => new { x = Round(v.x), y = Round(v.y), z = Round(v.z) };

    private static string PathOf(Transform t)
    {
        var names = new List<string>();
        for (var c = t; c != null; c = c.parent) names.Add(c.name);
        names.Reverse();
        return string.Join("/", names);
    }

    private static Transform StartSpot()
    {
        var physics = TrackManager.Instance?.GetCarPhysics() ?? UnityEngine.Object.FindObjectOfType<PrepareCarPhysics>();
        if (physics == null) return null;
        return physics.StartPosition != null ? physics.StartPosition : physics.carSpawnPosition;
    }

    private static object Relative(Vector3 p, Transform start)
    {
        if (start == null) return null;
        var d = p - start.position;
        return new { right = Round(Vector3.Dot(d, start.right)), up = Round(Vector3.Dot(d, start.up)), forward = Round(Vector3.Dot(d, start.forward)) };
    }

    private static object Pose(Transform t, Transform start) => t == null ? null : new Dictionary<string, object>
    {
        ["path"] = PathOf(t),
        ["position"] = Vec(t.position),
        ["euler"] = Vec(t.eulerAngles),
        ["forward"] = Vec(t.forward),
        ["relative"] = Relative(t.position, start),
    };

    [HarnessCommand("grid-start")]
    private static object Start(string args)
    {
        var track = TrackManager.Instance;
        var physics = track?.GetCarPhysics() ?? UnityEngine.Object.FindObjectOfType<PrepareCarPhysics>();
        var start = StartSpot();
        var race = track?.TryCast<RaceTrackManager>();
        var checkpoints = new List<object>();
        if (race?.checkPointsList != null)
            for (int i = 0; i < race.checkPointsList.Count; i++)
            {
                var cp = race.checkPointsList[i];
                if (cp != null) checkpoints.Add(new { index = i, name = cp.name, position = Vec(cp.transform.position), forward = Vec(cp.transform.forward), relative = Relative(cp.transform.position, start) });
            }
        return new Dictionary<string, object>
        {
            ["scene"] = SceneManager.GetActiveScene().name,
            ["track"] = track == null ? null : track.GetIl2CppType().Name,
            ["startPosition"] = Pose(start, start),
            ["carSpawnPosition"] = Pose(physics?.carSpawnPosition, start),
            ["startSource"] = physics?.StartPosition != null ? "StartPosition" : "carSpawnPosition",
            ["car"] = physics?.rigidBody == null ? null : Pose(physics.rigidBody.transform, start),
            ["checkpoints"] = checkpoints,
        };
    }

    // grid-find [regex|path=<regex>] [near=<metres>] [max=<n>]: objects whose name, mesh or material (or with path=, whose
    // full path) matches the regex, or with near=, every object with a renderer whose bounds come within that distance
    // of the start spot.
    [HarnessCommand("grid-find")]
    private static object Find(string args)
    {
        string pattern = DefaultPattern;
        float near = -1f;
        int max = 300;
        bool byPath = false;
        foreach (var part in (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.StartsWith("near=")) near = float.Parse(part.Substring(5), CultureInfo.InvariantCulture);
            else if (part.StartsWith("max=")) max = int.Parse(part.Substring(4), CultureInfo.InvariantCulture);
            else if (part.StartsWith("path=")) { byPath = true; pattern = part.Substring(5); }
            else pattern = part;
        }
        var regex = new Regex(pattern);
        var start = StartSpot();
        var found = new List<object>();
        int scanned = 0, matched = 0;
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            var scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
                Walk(root.transform, t =>
                {
                    scanned++;
                    var renderer = t.GetComponent<Renderer>();
                    var filter = t.GetComponent<MeshFilter>();
                    string mesh = filter?.sharedMesh?.name;
                    var materials = renderer == null ? new List<string>() : renderer.sharedMaterials.Where(m => m != null).Select(m => m.name).ToList();
                    var components = t.GetComponents<Component>().Select(c => c.GetIl2CppType().Name).Where(n => n != "Transform").ToList();
                    string decalMaterial = null;
                    if (components.Contains("DecalProjector"))
                    {
                        var decal = t.GetComponent<UnityEngine.Rendering.HighDefinition.DecalProjector>();
                        decalMaterial = decal?.material?.name;
                    }
                    bool hit;
                    float distance = -1f;
                    if (near > 0f)
                    {
                        if (start == null || (renderer == null && decalMaterial == null)) return;
                        distance = renderer != null ? Mathf.Sqrt(renderer.bounds.SqrDistance(start.position)) : Vector3.Distance(t.position, start.position);
                        hit = distance <= near;
                    }
                    else
                        hit = byPath ? regex.IsMatch(PathOf(t)) : regex.IsMatch(t.name) || (mesh != null && regex.IsMatch(mesh)) || materials.Any(m => regex.IsMatch(m)) || (decalMaterial != null && regex.IsMatch(decalMaterial));
                    if (!hit) return;
                    matched++;
                    if (found.Count >= max) return;
                    var entry = new Dictionary<string, object>
                    {
                        ["path"] = PathOf(t),
                        ["active"] = t.gameObject.activeInHierarchy,
                        ["position"] = Vec(t.position),
                        ["euler"] = Vec(t.eulerAngles),
                        ["scale"] = Vec(t.lossyScale),
                        ["relative"] = Relative(t.position, start),
                        ["components"] = components,
                    };
                    if (mesh != null) entry["mesh"] = mesh;
                    if (materials.Count > 0) entry["materials"] = materials;
                    if (decalMaterial != null) entry["decalMaterial"] = decalMaterial;
                    if (renderer != null) entry["bounds"] = new { center = Vec(renderer.bounds.center), size = Vec(renderer.bounds.size), relative = Relative(renderer.bounds.center, start) };
                    if (distance >= 0f) entry["distance"] = Round(distance);
                    found.Add(entry);
                });
        }
        return new Dictionary<string, object> { ["pattern"] = near > 0f ? null : pattern, ["near"] = near, ["scanned"] = scanned, ["matched"] = matched, ["objects"] = found };
    }

    private static void Walk(Transform t, Action<Transform> visit)
    {
        visit(t);
        for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), visit);
    }

    // grid-shot <file> <width> <height> <frame> <x> <y> <z> <a> <b> [fov|ortho=<halfHeight>]
    // frame "start": x,y,z are right/up/forward metres from the start spot, a = pitch down (deg), b = yaw from the start
    // spot's forward (deg). frame "world": world position, a = pitch, b = yaw. With ortho= the camera looks straight
    // down with the start spot's forward (or yaw b) as the image's up.
    [HarnessCommand("grid-shot")]
    private static object Shot(string args)
    {
        args = args ?? "";
        int cut = args.IndexOf(".png", StringComparison.OrdinalIgnoreCase);
        if (cut < 0) throw new ArgumentException("grid-shot needs a .png file path first");
        var p = new[] { args.Substring(0, cut + 4) }.Concat(args.Substring(cut + 4).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)).ToArray();
        if (p.Length < 10) throw new ArgumentException("usage: grid-shot <file> <w> <h> <start|world> <x> <y> <z> <pitch> <yaw> [fov|ortho=<halfHeight>] [frames=<n>]");
        float F(int i) => float.Parse(p[i], CultureInfo.InvariantCulture);
        string file = p[0];
        int w = int.Parse(p[1]), h = int.Parse(p[2]);
        var start = StartSpot();
        Vector3 position;
        float yawBase = 0f;
        if (p[3] == "start")
        {
            if (start == null) throw new InvalidOperationException("no start spot");
            position = start.position + start.right * F(4) + Vector3.up * F(5) + start.forward * F(6);
            var flat = Vector3.ProjectOnPlane(start.forward, Vector3.up);
            yawBase = Quaternion.LookRotation(flat).eulerAngles.y;
        }
        else position = new Vector3(F(4), F(5), F(6));
        float pitch = F(7), yaw = yawBase + F(8);
        float fov = 60f, ortho = -1f;
        int frames = 45;
        for (int i = 9; i < p.Length; i++)
        {
            if (p[i].StartsWith("ortho=")) ortho = float.Parse(p[i].Substring(6), CultureInfo.InvariantCulture);
            else if (p[i].StartsWith("frames=")) frames = int.Parse(p[i].Substring(7));
            else fov = F(i);
        }
        var rotation = ortho > 0f ? Quaternion.LookRotation(Vector3.down, Quaternion.Euler(0f, yaw, 0f) * Vector3.forward) : Quaternion.Euler(pitch, yaw, 0f);
        lastShot = null;
        lastShotError = null;
        MelonCoroutines.Start(Render(file, w, h, position, rotation, fov, ortho, frames));
        return new { file, position = Vec(position), euler = Vec(rotation.eulerAngles), fov, ortho, frames };
    }

    [HarnessCommand("grid-shot-state")]
    private static object ShotState(string args) => new { lastShot, lastShotError };

    private static IEnumerator Render(string file, int w, int h, Vector3 position, Quaternion rotation, float fov, float ortho, int frames)
    {
        var go = new GameObject("HarnessGridCamera");
        RenderTexture rt = null;
        Camera camera = null;
        bool failed = false;
        try
        {
            camera = go.AddComponent<Camera>();
            var main = Camera.main;
            if (main != null) camera.CopyFrom(main);
            rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = rt;
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.depth = -100f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 2000f;
            camera.orthographic = ortho > 0f;
            if (ortho > 0f) camera.orthographicSize = ortho;
            else camera.fieldOfView = fov;
            camera.enabled = true;
        }
        catch (Exception e)
        {
            lastShotError = e.ToString();
            failed = true;
        }
        if (!failed)
            for (int i = 0; i < frames; i++) yield return null;
        try
        {
            if (!failed)
            {
                camera.Render();
                var previous = RenderTexture.active;
                RenderTexture.active = rt;
                var texture = new Texture2D(w, h, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                texture.Apply();
                RenderTexture.active = previous;
                var png = ImageConversion.EncodeToPNG(texture);
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllBytes(file, png.ToArray());
                UnityEngine.Object.Destroy(texture);
                lastShot = file;
            }
        }
        catch (Exception e)
        {
            lastShotError = e.ToString();
        }
        finally
        {
            if (camera != null) camera.targetTexture = null;
            if (rt != null) rt.Release();
            UnityEngine.Object.Destroy(go);
            MelonLogger.Msg($"[Harness] grid-shot {file}: {(lastShotError == null ? "saved" : lastShotError.Split('\n')[0])}");
        }
    }

    // grid-mesh <object path> <right min> <right max> <forward min> <forward max>: the triangles of that object's mesh
    // (when the mesh is readable) whose vertices all lie in that box around the start spot, in start-spot coordinates.
    [HarnessCommand("grid-mesh")]
    private static object MeshNear(string args)
    {
        var p = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 5) throw new ArgumentException("usage: grid-mesh <path> <rightMin> <rightMax> <forwardMin> <forwardMax>");
        float F(int i) => float.Parse(p[i], CultureInfo.InvariantCulture);
        float r0 = F(1), r1 = F(2), f0 = F(3), f1 = F(4);
        var go = GameObject.Find(p[0]) ?? throw new ArgumentException($"no object {p[0]}");
        var mesh = go.GetComponent<MeshFilter>()?.sharedMesh ?? throw new ArgumentException("no mesh");
        var start = StartSpot() ?? throw new InvalidOperationException("no start spot");
        var result = new Dictionary<string, object> { ["mesh"] = mesh.name, ["readable"] = mesh.isReadable, ["vertexCount"] = mesh.vertexCount, ["subMeshes"] = mesh.subMeshCount };
        if (!mesh.isReadable) return result;
        var vertices = mesh.vertices;
        var local = new Vector3[vertices.Length];
        var matrix = go.transform.localToWorldMatrix;
        for (int i = 0; i < vertices.Length; i++)
        {
            var d = matrix.MultiplyPoint3x4(vertices[i]) - start.position;
            local[i] = new Vector3(Vector3.Dot(d, start.right), Vector3.Dot(d, Vector3.up), Vector3.Dot(d, start.forward));
        }
        bool Inside(Vector3 v) => v.x >= r0 && v.x <= r1 && v.z >= f0 && v.z <= f1;
        var triangles = new List<object>();
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var indices = mesh.GetTriangles(s);
            for (int i = 0; i + 2 < indices.Length; i += 3)
            {
                Vector3 a = local[indices[i]], b = local[indices[i + 1]], c = local[indices[i + 2]];
                if (!Inside(a) || !Inside(b) || !Inside(c)) continue;
                if (triangles.Count >= 4000) break;
                triangles.Add(new[] { Round(a.x), Round(a.z), Round(b.x), Round(b.z), Round(c.x), Round(c.z), Round((a.y + b.y + c.y) / 3f), s });
            }
        }
        result["triangles"] = triangles;
        return result;
    }

    private static Vector3? spawnOriginal;

    // grid-spawn-move <right> <forward> | reset: moves the race track's CarSpawnPosition (the spot the game's restart puts
    // the car on) relative to where the scene has it, dropped onto the ground below.
    [HarnessCommand("grid-spawn-move")]
    private static object SpawnMove(string args)
    {
        var physics = TrackManager.Instance?.GetCarPhysics() ?? throw new InvalidOperationException("no track car");
        var spawn = physics.carSpawnPosition ?? throw new InvalidOperationException("no car spawn position");
        spawnOriginal ??= spawn.position;
        var p = (args ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (p.Length == 1 && p[0] == "reset")
        {
            spawn.position = spawnOriginal.Value;
            return new { position = Vec(spawn.position), reset = true };
        }
        float right = float.Parse(p[0], CultureInfo.InvariantCulture), forward = float.Parse(p[1], CultureInfo.InvariantCulture);
        var target = spawnOriginal.Value + spawn.right * right + spawn.forward * forward;
        float ground = GroundBelow(target), originalGround = GroundBelow(spawnOriginal.Value);
        if (!float.IsNaN(ground) && !float.IsNaN(originalGround)) target.y = spawnOriginal.Value.y + ground - originalGround;
        spawn.position = target;
        return new { position = Vec(target), ground = Round(ground), originalGround = Round(originalGround) };
    }

    private static float GroundBelow(Vector3 p)
    {
        float best = float.NaN;
        foreach (var hit in Physics.RaycastAll(p + Vector3.up * 3f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore))
            if (hit.collider != null && hit.collider.attachedRigidbody == null && (float.IsNaN(best) || hit.point.y > best)) best = hit.point.y;
        return best;
    }
}
