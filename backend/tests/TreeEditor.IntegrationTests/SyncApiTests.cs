using System.Net.Http.Json;
using TreeEditor.Application.Dtos;

namespace TreeEditor.IntegrationTests;

[Collection("api")]
public sealed class SyncApiTests(TreeEditorApiFixture fixture) : ApiTestBase(fixture)
{
    [Fact]
    public async Task Check_version_is_stable_when_nothing_changed()
    {
        await ResetAsync();
        var root = await GetRootAsync();
        var ids = new List<int> { root.Id };
        ids.AddRange((await GetChildrenAsync(root.Id)).Select(child => child.Id));

        var first = await CheckAsync(ids.ToArray());
        var second = await CheckAsync(ids.ToArray());

        Assert.Equal(
            first.Nodes.OrderBy(n => n.Id).Select(n => n.Version),
            second.Nodes.OrderBy(n => n.Id).Select(n => n.Version));
    }

    [Fact]
    public async Task Value_change_changes_the_node_version()
    {
        await ResetAsync();
        var leaf = await FindLeafAsync();
        var before = await GetElementAsync(leaf.Id);

        await ApplyAsync(new ApplyRequest([new UpdateOperation(leaf.Id, "changed")], [], []));

        var check = await CheckAsync(leaf.Id);
        Assert.NotEqual(before.Version, check.Nodes.Single().Version);
        Assert.Empty(check.Deleted);
    }

    [Fact]
    public async Task Adding_a_child_changes_the_parent_version()
    {
        await ResetAsync();
        var root = await GetRootAsync();
        var before = await GetElementAsync(root.Id);

        await ApplyAsync(new ApplyRequest([], [new AddOperation(-1, root.Id, "added-node")], []));

        var check = await CheckAsync(root.Id);
        Assert.NotEqual(before.Version, check.Nodes.Single().Version);
    }

    [Fact]
    public async Task Deleting_a_subtree_reports_descendants_as_deleted()
    {
        await ResetAsync();
        var branch = await FindNonRootBranchAsync();
        var descendants = await GetDescendantsAsync(branch.Id);

        await ApplyAsync(new ApplyRequest([], [], [branch.Id]));

        var ids = new List<int> { branch.Id, 2_000_000_000 };
        ids.AddRange(descendants.Select(d => d.Id));

        var check = await CheckAsync(ids.ToArray());

        Assert.Contains(branch.Id, check.Deleted);
        Assert.Contains(2_000_000_000, check.Deleted);
        Assert.All(descendants, descendant => Assert.Contains(descendant.Id, check.Deleted));
    }

    [Fact]
    public async Task Check_only_reports_the_touched_node_version()
    {
        await ResetAsync();
        var root = await GetRootAsync();
        var children = await GetChildrenAsync(root.Id);
        Assert.True(children.Count >= 2);
        var target = children[0];
        var other = children[1];

        var before = await CheckAsync(target.Id, other.Id);
        await ApplyAsync(new ApplyRequest([new UpdateOperation(target.Id, "changed")], [], []));
        var after = await CheckAsync(target.Id, other.Id);

        Assert.NotEqual(
            before.Nodes.Single(n => n.Id == target.Id).Version,
            after.Nodes.Single(n => n.Id == target.Id).Version);
        Assert.Equal(
            before.Nodes.Single(n => n.Id == other.Id).Version,
            after.Nodes.Single(n => n.Id == other.Id).Version);
    }

    [Fact]
    public async Task SignalR_hub_is_mapped()
    {
        var response = await Client.PostAsync("/hubs/tree/negotiate?negotiateVersion=1", content: null);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("connectionId", body);
    }

    [Fact]
    public async Task Revision_increases_on_apply_and_reset()
    {
        await ResetAsync();
        var r1 = await GetRevisionAsync();

        await ApplyAsync(new ApplyRequest([new UpdateOperation((await GetRootAsync()).Id, "root-renamed")], [], []));
        var r2 = await GetRevisionAsync();

        await ResetAsync();
        var r3 = await GetRevisionAsync();

        Assert.True(r2 > r1);
        Assert.True(r3 > r2);
    }

    private async Task<TreeCheckResult> CheckAsync(params int[] ids)
    {
        var response = await Client.PostAsJsonAsync("/api/tree/check", new TreeCheckRequest(ids));
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<TreeCheckResult>();
        Assert.NotNull(result);
        return result!;
    }

    private async Task<long> GetRevisionAsync()
    {
        var dto = await Client.GetFromJsonAsync<RevisionDto>("/api/tree/revision");
        Assert.NotNull(dto);
        return dto!.Revision;
    }
}
