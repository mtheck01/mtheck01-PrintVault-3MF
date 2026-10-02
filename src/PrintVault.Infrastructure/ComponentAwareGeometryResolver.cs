using System.IO.Compression;
using System.Xml.Linq;

namespace PrintVault.Infrastructure;

/// <summary>Package-level geometry resolver for 3MF component-based models.
/// Read-only and deliberately scoped to geometry/readiness; it does not classify models.</summary>
public sealed class ComponentAwareGeometryResolver
{
    private const int MaxDepth = 256;

    public GeometryResolution Resolve(string file)
    {
        using var zip = ZipFile.OpenRead(file);
        var models = new Dictionary<string, ModelPart>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries.Where(e => e.FullName.StartsWith("3D/", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".model", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = entry.Open();
            models[entry.FullName] = ParseModel(XDocument.Load(stream, LoadOptions.None));
        }

        if (models.Count == 0) return new GeometryResolution(0, 0, 0, 0, 0, 0, 0);

        var global = new Dictionary<int, List<string>>();
        foreach (var pair in models)
            foreach (var id in pair.Value.Objects.Keys)
            {
                if (!global.TryGetValue(id, out var list)) global[id] = list = new List<string>();
                list.Add(pair.Key);
            }

        var relationships = ReadRelationships(zip);
        long reachableVertices = 0, reachableTriangles = 0;
        int buildItems = 0, componentRefs = 0, unresolved = 0, cycles = 0;

        foreach (var pair in models)
        {
            buildItems += pair.Value.Build.Count;
            foreach (var id in pair.Value.Build)
            {
                var seen = new HashSet<(string Path, int Id)>();
                var g = Walk(pair.Key, id, models, global, relationships, seen, 0, ref componentRefs, ref unresolved, ref cycles);
                reachableVertices += g.V;
                reachableTriangles += g.T;
            }
        }

        return new GeometryResolution(models.Count, buildItems, componentRefs, unresolved, cycles, reachableVertices, reachableTriangles);
    }

    private static (long V, long T) Walk(string modelPath, int id, Dictionary<string, ModelPart> models, Dictionary<int, List<string>> global, Dictionary<string, List<string>> relationships, HashSet<(string Path, int Id)> seen, int depth, ref int refs, ref int unresolved, ref int cycles)
    {
        if (depth > MaxDepth) return (0, 0);
        if (!seen.Add((modelPath, id))) { cycles++; return (0, 0); }
        if (!models.TryGetValue(modelPath, out var model) || !model.Objects.TryGetValue(id, out var obj)) { unresolved++; return (0, 0); }

        long v = obj.Vertices, t = obj.Triangles;
        foreach (var target in obj.Components)
        {
            refs++;
            if (model.Objects.ContainsKey(target))
            {
                var nested = Walk(modelPath, target, models, global, relationships, seen, depth + 1, ref refs, ref unresolved, ref cycles);
                v += nested.V; t += nested.T;
                continue;
            }

            var candidates = global.TryGetValue(target, out var paths)
                ? paths.Where(p => !string.Equals(p, modelPath, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                : new List<string>();
            if (candidates.Count == 1 && RelationshipAllows(modelPath, candidates[0], relationships))
            {
                var targetPath = candidates[0];
                var nested = Walk(targetPath, target, models, global, relationships, seen, depth + 1, ref refs, ref unresolved, ref cycles);
                v += nested.V; t += nested.T;
            }
            else if (candidates.Count == 1)
            {
                unresolved++;
            }
            else if (candidates.Count == 0) unresolved++;
        }
        return (v, t);
    }

    private static bool RelationshipAllows(string source, string target, Dictionary<string, List<string>> relationships)
    {
        if (!relationships.TryGetValue(source, out var targets)) return false;
        var normalized = NormalizePath(target);
        return targets.Any(x => string.Equals(NormalizePath(x), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private static Dictionary<string, List<string>> ReadRelationships(ZipArchive zip)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)))
        {
            var source = RelationshipSource(entry.FullName);
            if (source == null) continue;
            try
            {
                using var stream = entry.Open();
                var doc = XDocument.Load(stream);
                var targets = new List<string>();
                foreach (var rel in doc.Descendants().Where(x => x.Name.LocalName == "Relationship"))
                {
                    var target = (string?)rel.Attribute("Target");
                    if (string.IsNullOrWhiteSpace(target)) continue;
                    var baseDir = Path.GetDirectoryName(source.Replace('/', '\\'))?.Replace('\\', '/') ?? string.Empty;
                    targets.Add(NormalizePath(baseDir.Length == 0 ? target : baseDir + "/" + target));
                }
                result[source] = targets;
            }
            catch { }
        }
        return result;
    }

    private static string? RelationshipSource(string relationshipPath)
    {
        var path = relationshipPath.Replace('\\', '/');
        if (!path.StartsWith("3D/", StringComparison.OrdinalIgnoreCase) || !path.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)) return null;
        var index = path.LastIndexOf("/_rels/", StringComparison.OrdinalIgnoreCase);
        if (index < 0) return null;
        return path[..index] + "/" + path[(index + 7)..];
    }

    private static string NormalizePath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
        for (int i = 0; i < parts.Count;)
        {
            if (parts[i] == ".") { parts.RemoveAt(i); continue; }
            if (parts[i] == ".." && i > 0) { parts.RemoveAt(i); parts.RemoveAt(i - 1); i--; continue; }
            i++;
        }
        return string.Join('/', parts).TrimStart('/');
    }

    private static ModelPart ParseModel(XDocument doc)
    {
        var objects = new Dictionary<int, Obj>();
        var resources = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "resources");
        if (resources != null)
            foreach (var element in resources.Elements().Where(e => e.Name.LocalName == "object"))
            {
                if (!int.TryParse((string?)element.Attribute("id"), out var id)) continue;
                var mesh = element.Elements().FirstOrDefault(e => e.Name.LocalName == "mesh");
                var vertices = mesh?.Elements().FirstOrDefault(e => e.Name.LocalName == "vertices")?.Elements().Count(e => e.Name.LocalName == "vertex") ?? 0;
                var triangles = mesh?.Elements().FirstOrDefault(e => e.Name.LocalName == "triangles")?.Elements().Count(e => e.Name.LocalName == "triangle") ?? 0;
                var components = element.Elements().FirstOrDefault(e => e.Name.LocalName == "components")?.Elements().Where(e => e.Name.LocalName == "component").Select(e => int.TryParse((string?)e.Attribute("objectid"), out var objectId) ? objectId : -1).Where(x => x >= 0).ToList() ?? new List<int>();
                objects[id] = new Obj(vertices, triangles, components);
            }

        var build = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "build")?.Elements().Where(e => e.Name.LocalName == "item").Select(e => int.TryParse((string?)e.Attribute("objectid"), out var id) ? id : -1).Where(x => x >= 0).ToList() ?? new List<int>();
        return new ModelPart(objects, build);
    }

    private sealed record Obj(long Vertices, long Triangles, List<int> Components);
    private sealed record ModelPart(Dictionary<int, Obj> Objects, List<int> Build);
}

public sealed record GeometryResolution(int ModelParts, int BuildItems, int ComponentReferences, int UnresolvedReferences, int Cycles, long ReachableVertices, long ReachableTriangles);
