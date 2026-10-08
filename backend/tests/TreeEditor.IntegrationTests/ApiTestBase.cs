using System.Net;
using System.Net.Http.Json;
using TreeEditor.Application.Dtos;

namespace TreeEditor.IntegrationTests;

/// <summary>
/// Shared helpers for the integration tests. The sample data is large and not hard-coded, so the
/// tests discover structure dynamically (roots, leaves, branches, deep nodes) instead of relying on
/// specific ids/values.
/// </summary>
public abstract class ApiTestBase
{
    protected ApiTestBase(TreeEditorApiFixture fixture)
    {
        Fixture = fixture;
    }

    protected TreeEditorApiFixture Fixture { get; }

    protected HttpClient Client => Fixture.Client;

    protected Task ResetAsync() => Fixture.ResetAsync();

    protected async Task<IReadOnlyList<ElementNodeDto>> GetRootsAsync()
    {
        var roots = await Client.GetFromJsonAsync<List<ElementNodeDto>>("/api/tree/roots");
        Assert.NotNull(roots);
        return roots!;
    }

    protected async Task<ElementNodeDto> GetRootAsync() => Assert.Single(await GetRootsAsync());

    protected async Task<IReadOnlyList<ElementNodeDto>> GetChildrenAsync(int id)
    {
        var children = await Client.GetFromJsonAsync<List<ElementNodeDto>>($"/api/tree/{id}/children");
        Assert.NotNull(children);
        return children!;
    }

    protected async Task<ElementNodeDto> GetElementAsync(int id)
    {
        var element = await Client.GetFromJsonAsync<ElementNodeDto>($"/api/tree/{id}");
        Assert.NotNull(element);
        return element!;
    }

    protected async Task<ApplyResult> ApplyAsync(ApplyRequest request)
    {
        var response = await Client.PostAsJsonAsync("/api/tree/apply", request);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ApplyResult>();
        Assert.NotNull(result);
        return result!;
    }

    protected async Task<ElementNodeDto> FindLeafAsync()
    {
        var found = await BfsAsync((node, _) => !node.HasChildren);
        Assert.NotNull(found);
        return found!.Value.Node;
    }

    protected async Task<ElementNodeDto> FindNonRootBranchAsync()
    {
        var found = await BfsAsync((node, depth) => depth > 0 && node.HasChildren);
        Assert.NotNull(found);
        return found!.Value.Node;
    }

    protected async Task<(ElementNodeDto Node, int Depth)> FindNodeAtLeastDepthAsync(int minDepth)
    {
        var found = await BfsAsync((_, depth) => depth >= minDepth);
        Assert.NotNull(found);
        return found!.Value;
    }

    protected async Task<IReadOnlyList<ElementNodeDto>> GetDescendantsAsync(int id, int max = 25)
    {
        var result = new List<ElementNodeDto>();
        var queue = new Queue<int>();
        queue.Enqueue(id);
        while (queue.Count > 0 && result.Count < max)
        {
            foreach (var child in await GetChildrenAsync(queue.Dequeue()))
            {
                result.Add(child);
                if (child.HasChildren)
                {
                    queue.Enqueue(child.Id);
                }

                if (result.Count >= max)
                {
                    break;
                }
            }
        }

        return result;
    }

    protected async Task<HttpStatusCode> GetStatusCodeAsync(int id) =>
        (await Client.GetAsync($"/api/tree/{id}")).StatusCode;

    private async Task<(ElementNodeDto Node, int Depth)?> BfsAsync(
        Func<ElementNodeDto, int, bool> match,
        int maxNodes = 3000)
    {
        var queue = new Queue<(ElementNodeDto Node, int Depth)>();
        foreach (var root in await GetRootsAsync())
        {
            queue.Enqueue((root, 0));
        }

        var visited = 0;
        while (queue.Count > 0 && visited < maxNodes)
        {
            var (node, depth) = queue.Dequeue();
            visited++;

            if (match(node, depth))
            {
                return (node, depth);
            }

            if (!node.HasChildren)
            {
                continue;
            }

            foreach (var child in await GetChildrenAsync(node.Id))
            {
                queue.Enqueue((child, depth + 1));
            }
        }

        return null;
    }
}
