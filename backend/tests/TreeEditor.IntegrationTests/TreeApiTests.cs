using System.Net;
using System.Net.Http.Json;
using TreeEditor.Application.Dtos;

namespace TreeEditor.IntegrationTests;

[Collection("api")]
public sealed class TreeApiTests(TreeEditorApiFixture fixture) : ApiTestBase(fixture)
{
    private static readonly UpdateOperation[] NoUpdates = [];
    private static readonly AddOperation[] NoAdditions = [];
    private static readonly int[] NoDeletions = [];

    [Fact]
    public async Task Sample_data_is_at_least_four_levels_deep()
    {
        await ResetAsync();

        var (node, depth) = await FindNodeAtLeastDepthAsync(4);

        Assert.True(depth >= 4);
        Assert.True(node.Id > 0);
    }

    [Fact]
    public async Task Roots_returns_a_single_root_with_children()
    {
        await ResetAsync();

        var root = await GetRootAsync();

        Assert.Null(root.ParentId);
        Assert.True(root.HasChildren);
    }

    [Fact]
    public async Task Children_returns_direct_children_only()
    {
        await ResetAsync();
        var root = await GetRootAsync();

        var children = await GetChildrenAsync(root.Id);
        Assert.NotEmpty(children);
        Assert.All(children, child => Assert.Equal(root.Id, child.ParentId));

        // None of the grandchildren may appear in the parent's child list.
        var grandchildIds = new HashSet<int>();
        foreach (var child in children.Where(c => c.HasChildren))
        {
            foreach (var grandchild in await GetChildrenAsync(child.Id))
            {
                grandchildIds.Add(grandchild.Id);
            }
        }

        Assert.DoesNotContain(children, child => grandchildIds.Contains(child.Id));
    }

    [Fact]
    public async Task Apply_updates_value()
    {
        await ResetAsync();
        var leaf = await FindLeafAsync();
        var updated = $"{leaf.Value}-edited";

        var result = await ApplyAsync(new ApplyRequest(
            [new UpdateOperation(leaf.Id, updated)],
            NoAdditions,
            NoDeletions));

        Assert.Equal(1, result.UpdatedCount);
        Assert.Equal(updated, (await GetElementAsync(leaf.Id)).Value);
    }

    [Fact]
    public async Task Apply_adds_child_and_returns_id_mapping()
    {
        await ResetAsync();
        var root = await GetRootAsync();

        var result = await ApplyAsync(new ApplyRequest(
            NoUpdates,
            [new AddOperation(-1, root.Id, "added-node")],
            NoDeletions));

        var added = Assert.Single(result.Added);
        Assert.Equal(-1, added.TempId);
        Assert.True(added.Id > 0);

        var children = await GetChildrenAsync(root.Id);
        Assert.Contains(children, child => child.Id == added.Id && child.Value == "added-node");
    }

    [Fact]
    public async Task Apply_supports_adding_child_to_a_pending_addition()
    {
        await ResetAsync();
        var root = await GetRootAsync();

        var result = await ApplyAsync(new ApplyRequest(
            NoUpdates,
            [
                new AddOperation(-1, root.Id, "pending-parent"),
                new AddOperation(-2, -1, "pending-child"),
            ],
            NoDeletions));

        var parent = result.Added.Single(a => a.TempId == -1);
        var child = result.Added.Single(a => a.TempId == -2);

        var children = await GetChildrenAsync(parent.Id);
        Assert.Contains(children, c => c.Id == child.Id && c.Value == "pending-child");
    }

    [Fact]
    public async Task Apply_supports_child_listed_before_its_pending_parent()
    {
        await ResetAsync();
        var root = await GetRootAsync();

        // The child (-2) references a parent (-1) that appears later in the batch.
        var result = await ApplyAsync(new ApplyRequest(
            NoUpdates,
            [
                new AddOperation(-2, -1, "pending-child"),
                new AddOperation(-1, root.Id, "pending-parent"),
            ],
            NoDeletions));

        var parent = result.Added.Single(a => a.TempId == -1);
        var child = result.Added.Single(a => a.TempId == -2);

        var children = await GetChildrenAsync(parent.Id);
        Assert.Contains(children, c => c.Id == child.Id && c.Value == "pending-child");
    }

    [Fact]
    public async Task Deleting_an_element_cascades_to_descendants()
    {
        await ResetAsync();
        var branch = await FindNonRootBranchAsync();
        var descendants = await GetDescendantsAsync(branch.Id);
        Assert.NotEmpty(descendants);

        var result = await ApplyAsync(new ApplyRequest(NoUpdates, NoAdditions, [branch.Id]));

        Assert.True(result.DeletedCount >= descendants.Count + 1);
        Assert.Equal(HttpStatusCode.NotFound, await GetStatusCodeAsync(branch.Id));
        foreach (var descendant in descendants)
        {
            Assert.Equal(HttpStatusCode.NotFound, await GetStatusCodeAsync(descendant.Id));
        }
    }

    [Fact]
    public async Task Deleted_elements_cannot_be_edited()
    {
        await ResetAsync();
        var branch = await FindNonRootBranchAsync();
        await ApplyAsync(new ApplyRequest(NoUpdates, NoAdditions, [branch.Id]));

        var response = await Client.PostAsJsonAsync(
            "/api/tree/apply",
            new ApplyRequest([new UpdateOperation(branch.Id, "no longer editable")], NoAdditions, NoDeletions));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Apply_is_atomic_when_an_operation_is_invalid()
    {
        await ResetAsync();
        var leaf = await FindLeafAsync();
        var original = leaf.Value;

        var response = await Client.PostAsJsonAsync(
            "/api/tree/apply",
            new ApplyRequest(
                [new UpdateOperation(leaf.Id, "should be rolled back")],
                [new AddOperation(-1, 2_000_000_000, "orphan")],
                NoDeletions));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(original, (await GetElementAsync(leaf.Id)).Value);
    }

    [Fact]
    public async Task Adding_a_child_sets_has_children_on_the_parent()
    {
        await ResetAsync();
        var leaf = await FindLeafAsync();
        Assert.False(leaf.HasChildren);

        var result = await ApplyAsync(new ApplyRequest(
            NoUpdates,
            [new AddOperation(-1, leaf.Id, "first-child")],
            NoDeletions));

        var parent = await GetElementAsync(leaf.Id);
        Assert.True(parent.HasChildren);
        Assert.Contains(await GetChildrenAsync(leaf.Id), child => child.Id == result.Added.Single().Id);
    }

    [Fact]
    public async Task Reset_restores_the_original_sample_data()
    {
        await ResetAsync();
        var leaf = await FindLeafAsync();
        var original = leaf.Value;

        await ApplyAsync(new ApplyRequest([new UpdateOperation(leaf.Id, "temporary")], NoAdditions, NoDeletions));
        Assert.Equal("temporary", (await GetElementAsync(leaf.Id)).Value);

        await ResetAsync();

        Assert.Equal(original, (await GetElementAsync(leaf.Id)).Value);
    }
}
