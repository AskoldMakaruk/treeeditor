// Builds data/sample-tree.json from a public repository's directory/file tree.
//
// A repository tree is a natural hierarchy: directories contain files and other directories,
// several levels deep. The commit is pinned so the sample data is stable, and each path is
// rewritten into the {id, value, parentId} element schema used by the seeder.
//
// Run from the repository root:
//     dotnet run scripts/build-sample-tree.cs

using System.Text.Json.Nodes;

const string Repo = "sveltejs/svelte";
const string Commit = "707c28146b0f0a6d5404a1bd4769874c3c24851a";
const string OutputPath = "data/sample-tree.json";

using var http = new HttpClient();
http.DefaultRequestHeaders.UserAgent.ParseAdd("tree-editor-sample");
http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

using var response = await http.GetAsync(
    $"https://api.github.com/repos/{Repo}/git/trees/{Commit}?recursive=1");
response.EnsureSuccessStatusCode();

using var stream = await response.Content.ReadAsStreamAsync();
var tree = JsonNode.Parse(stream) ?? throw new InvalidOperationException("Could not fetch the tree.");

var entries = tree["tree"]?.AsArray()
    ?? throw new InvalidOperationException("The response has no 'tree' array.");

// Parents before children, so ids (and inserts) are ordered parent-first.
var paths = entries
    .Where(node => node?["type"]?.GetValue<string>() is "blob" or "tree")
    .Select(node => node!["path"]!.GetValue<string>())
    .OrderBy(path => path.Count(character => character == '/'))
    .ThenBy(path => path, StringComparer.Ordinal)
    .ToList();

var rootValue = Repo.Split('/')[1];
var nodes = new List<(int Id, string Value, int? ParentId)> { (1, rootValue, null) };
var pathToId = new Dictionary<string, int> { [""] = 1 };
var nextId = 2;

foreach (var path in paths)
{
    if (pathToId.ContainsKey(path))
    {
        continue;
    }

    var separator = path.LastIndexOf('/');
    var parentPath = separator < 0 ? string.Empty : path[..separator];
    var value = separator < 0 ? path : path[(separator + 1)..];

    pathToId[path] = nextId;
    nodes.Add((nextId, value, pathToId[parentPath]));
    nextId++;
}

var nodesArray = new JsonArray();
foreach (var node in nodes)
{
    nodesArray.Add((JsonNode)new JsonObject
    {
        ["id"] = node.Id,
        ["value"] = node.Value,
        ["parentId"] = node.ParentId is int parentId ? JsonValue.Create(parentId) : null,
    });
}

var document = new JsonObject
{
    ["source"] = $"github:{Repo}@{Commit}",
    ["description"] = "Repository directory/file hierarchy used as initial sample data.",
    ["rootId"] = 1,
    ["count"] = nodes.Count,
    ["nodes"] = nodesArray,
};

Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(OutputPath))!);
await File.WriteAllTextAsync(OutputPath, document.ToJsonString());

Console.WriteLine($"wrote {nodes.Count} nodes from {Repo}@{Commit} to {OutputPath}");
