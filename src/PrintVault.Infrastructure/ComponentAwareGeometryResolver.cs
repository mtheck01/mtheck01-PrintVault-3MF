using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;

namespace PrintVault.Infrastructure;

/// <summary>Package-level geometry resolver for 3MF component-based models.
/// Read-only and deliberately scoped to geometry/readiness; it does not classify models.</summary>
public sealed class ComponentAwareGeometryResolver
{
    private const int MaxDepth = 256;

    public GeometryResolution Resolve(string file)
        => Resolve(file, null, stopAfterFirstReachableGeometry: false);

    public GeometryResolution Resolve(string file, XDocument? primaryModel, bool stopAfterFirstReachableGeometry)
    {
        using var zip = ZipFile.OpenRead(file);
        if (stopAfterFirstReachableGeometry)
            return ResolveReachableGeometry(zip, primaryModel);

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

    private static GeometryResolution ResolveReachableGeometry(ZipArchive zip, XDocument? primaryModel)
    {
        const string mainPath = "3D/3dmodel.model";
        if (primaryModel is null)
        {
            var mainEntry = zip.GetEntry(mainPath);
            if (mainEntry is null) return new GeometryResolution(0, 0, 0, 0, 0, 0, 0) { CountsExact = false };
            using var stream = mainEntry.Open();
            primaryModel = XDocument.Load(stream, LoadOptions.None);
        }

        var main = ParseModel(primaryModel);
        if (main.Build.Count == 0) return new GeometryResolution(1, 0, 0, 0, 0, 0, 0) { CountsExact = false };

        var relationships = ReadRelationships(zip);
        var objectCache = new Dictionary<(string Path, int Id), Obj>();
        var componentRefs = 0;
        var unresolved = 0;
        var cycles = 0;
        var seen = new HashSet<(string Path, int Id)>();
        foreach (var id in main.Build)
        {
            if (HasReachableGeometry(zip, mainPath, id, main, relationships, objectCache, seen, 0,
                    ref componentRefs, ref unresolved, ref cycles))
            {
                // Production classification needs a yes/no readiness signal. Exact counts
                // remain available from the forensic overload, which does not early-exit.
                return new GeometryResolution(1, main.Build.Count, componentRefs, unresolved, cycles, 1, 0)
                    { CountsExact = false };
            }
        }

        return new GeometryResolution(1, main.Build.Count, componentRefs, unresolved, cycles, 0, 0)
            { CountsExact = false };
    }

    private static bool HasReachableGeometry(
        ZipArchive zip,
        string modelPath,
        int id,
        ModelPart main,
        Dictionary<string, List<string>> relationships,
        Dictionary<(string Path, int Id), Obj> objectCache,
        HashSet<(string Path, int Id)> seen,
        int depth,
        ref int refs,
        ref int unresolved,
        ref int cycles)
    {
        if (depth > MaxDepth) return false;
        var key = (modelPath, id);
        if (!seen.Add(key)) { cycles++; return false; }
        try
        {
            Obj? obj;
            if (string.Equals(modelPath, "3D/3dmodel.model", StringComparison.OrdinalIgnoreCase))
                main.Objects.TryGetValue(id, out obj);
            else if (!objectCache.TryGetValue(key, out obj))
            {
                var entry = zip.GetEntry(modelPath);
                if (entry is null) { unresolved++; return false; }
                using var stream = entry.Open();
                obj = ReadObjectProbe(stream, id);
                if (obj is null) { unresolved++; return false; }
                objectCache[key] = obj;
            }

            if (obj is null) { unresolved++; return false; }
            if (obj.Vertices > 0) return true;
            foreach (var component in obj.Components)
            {
                refs++;
                string targetModel;
                if (!string.IsNullOrWhiteSpace(component.Path))
                {
                    targetModel = ResolveComponentPath(modelPath, component.Path);
                    if (!RelationshipAllows(modelPath, targetModel, relationships) || zip.GetEntry(targetModel) is null)
                    {
                        unresolved++;
                        continue;
                    }
                }
                else
                {
                    targetModel = modelPath;
                    if (string.Equals(modelPath, "3D/3dmodel.model", StringComparison.OrdinalIgnoreCase) &&
                        !main.Objects.ContainsKey(component.ObjectId))
                    {
                        unresolved++;
                        continue;
                    }
                }

                if (HasReachableGeometry(zip, targetModel, component.ObjectId, main, relationships,
                        objectCache, seen, depth + 1, ref refs, ref unresolved, ref cycles))
                    return true;
            }
            return false;
        }
        finally { seen.Remove(key); }
    }

    private static Obj? ReadObjectProbe(Stream stream, int objectId)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true
        });

        var objectDepth = -1;
        var vertices = 0L;
        var components = new List<ComponentReference>();
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (objectDepth < 0 && reader.LocalName.Equals("object", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(reader.GetAttribute("id"), out var foundId) && foundId == objectId)
                {
                    objectDepth = reader.Depth;
                    if (reader.IsEmptyElement) return new Obj(0, 0, components);
                    continue;
                }

                if (objectDepth < 0 || reader.Depth <= objectDepth) continue;
                if (reader.LocalName.Equals("vertex", StringComparison.OrdinalIgnoreCase))
                {
                    // Any vertex in the referenced object's mesh is enough for production
                    // readiness; stop before decompressing the rest of a very large XML part.
                    vertices = 1;
                    return new Obj(vertices, 0, components);
                }
                if (reader.LocalName.Equals("component", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(reader.GetAttribute("objectid"), out var childId) && childId >= 0)
                {
                    var path = reader.GetAttribute("path");
                    if (path is null)
                    {
                        for (var i = 0; i < reader.AttributeCount; i++)
                        {
                            reader.MoveToAttribute(i);
                            if (reader.LocalName.Equals("path", StringComparison.OrdinalIgnoreCase))
                            {
                                path = reader.Value;
                                break;
                            }
                        }
                        reader.MoveToElement();
                    }
                    components.Add(new ComponentReference(childId, path));
                }
            }
            else if (reader.NodeType == XmlNodeType.EndElement && objectDepth == reader.Depth &&
                     reader.LocalName.Equals("object", StringComparison.OrdinalIgnoreCase))
            {
                return new Obj(vertices, 0, components);
            }
        }
        return objectDepth < 0 ? null : new Obj(vertices, 0, components);
    }

    private static string ResolveComponentPath(string sourceModel, string componentPath)
    {
        if (componentPath.StartsWith('/') || componentPath.StartsWith('\\'))
            return NormalizePath(componentPath);
        var baseDir = Path.GetDirectoryName(sourceModel.Replace('/', '\\'))?.Replace('\\', '/') ?? string.Empty;
        return NormalizePath(baseDir.Length == 0 ? componentPath : baseDir + "/" + componentPath);
    }

    private static (long V, long T) Walk(string modelPath, int id, Dictionary<string, ModelPart> models, Dictionary<int, List<string>> global, Dictionary<string, List<string>> relationships, HashSet<(string Path, int Id)> seen, int depth, ref int refs, ref int unresolved, ref int cycles)
    {
        if (depth > MaxDepth) return (0, 0);

        var key = (modelPath, id);
        // Track only the current recursion stack, not every node visited by sibling
        // branches. The same component can legitimately be instantiated multiple times;
        // a global visited set incorrectly classified shared references as cycles and
        // under-counted reachable geometry.
        if (!seen.Add(key)) { cycles++; return (0, 0); }

        try
        {
            if (!models.TryGetValue(modelPath, out var model) || !model.Objects.TryGetValue(id, out var obj))
            {
                unresolved++;
                return (0, 0);
            }

            long v = obj.Vertices, t = obj.Triangles;
            foreach (var component in obj.Components)
            {
                refs++;
                var target = component.ObjectId;
                if (!string.IsNullOrWhiteSpace(component.Path))
                {
                    var targetPath = ResolveComponentPath(modelPath, component.Path);
                    if (RelationshipAllows(modelPath, targetPath, relationships) && models.ContainsKey(targetPath))
                    {
                        var nested = Walk(targetPath, target, models, global, relationships, seen, depth + 1, ref refs, ref unresolved, ref cycles);
                        v += nested.V; t += nested.T;
                    }
                    else unresolved++;
                    continue;
                }
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
                else if (candidates.Count == 0)
                {
                    unresolved++;
                }
            }
            return (v, t);
        }
        finally
        {
            seen.Remove(key);
        }
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
                    // Package-root targets (the common 3MF form) are already absolute
                    // within the archive. Resolve only relative targets against the
                    // relationship owner's directory.
                    var resolvedTarget = target.StartsWith('/') || target.StartsWith('\\')
                        ? target
                        : baseDir.Length == 0 ? target : baseDir + "/" + target;
                    targets.Add(NormalizePath(resolvedTarget));
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
        var owner = path[..index] + "/" + path[(index + 7)..];
        return owner.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)
            ? owner[..^".rels".Length]
            : owner;
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
                var components = element.Elements().FirstOrDefault(e => e.Name.LocalName == "components")?.Elements().Where(e => e.Name.LocalName == "component").Select(e => new ComponentReference(
                    int.TryParse((string?)e.Attribute("objectid"), out var objectId) ? objectId : -1,
                    e.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals("path", StringComparison.OrdinalIgnoreCase))?.Value)).Where(x => x.ObjectId >= 0).ToList() ?? new List<ComponentReference>();
                objects[id] = new Obj(vertices, triangles, components);
            }

        var build = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "build")?.Elements().Where(e => e.Name.LocalName == "item").Select(e => int.TryParse((string?)e.Attribute("objectid"), out var id) ? id : -1).Where(x => x >= 0).ToList() ?? new List<int>();
        return new ModelPart(objects, build);
    }

    private sealed record ComponentReference(int ObjectId, string? Path);
    private sealed record Obj(long Vertices, long Triangles, List<ComponentReference> Components);
    private sealed record ModelPart(Dictionary<int, Obj> Objects, List<int> Build);
}

public sealed record GeometryResolution(int ModelParts, int BuildItems, int ComponentReferences, int UnresolvedReferences, int Cycles, long ReachableVertices, long ReachableTriangles)
{
    public bool CountsExact { get; init; } = true;
}
