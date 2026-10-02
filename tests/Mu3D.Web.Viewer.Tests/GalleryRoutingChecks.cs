using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Logging.Abstractions;
using Mu3D.GalleryApp.Web.Infrastructure;

// This recording renderer checks real Blazor lifecycle behavior without a browser or GPU.
#pragma warning disable BL0006
internal static class GalleryRoutingChecks
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(GridPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SearchPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(ContentHost))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(SnapshotPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(GalleryPage))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CascadingValue<string>))]
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(RouteView))]
    internal static async Task Validate(Action<bool, string> check)
    {
        var navigation = new TestNavigation();
        var pages = new List<SnapshotPage>();
        await using var renderer = new RecordingRenderer(navigation, pages);
        await renderer.Dispatcher.InvokeAsync(async () =>
        {
            int root = renderer.Attach(new ContentHost());
            var gridRoute = new RouteData(typeof(GridPage), new Dictionary<string, object?>());
            RenderFragment gridContent = GalleryRouteView.Content(gridRoute, "examples/grid-helper");
            await renderer.Render(root, gridContent);
            SnapshotPage grid = pages.Single();
            check(grid.Path == "examples/grid-helper" && grid.Id == "grid-helper", "Matched routes identify the rendered Grid page.");

            // A search changes the global location before Router supplies the new matched content.
            navigation.NavigateTo("examples?search=grid", replace: true);
            await renderer.Render(root, gridContent);
            grid.Refresh();
            check(pages.Count == 1 && grid.Disposals == 0 && grid.Id == "grid-helper" &&
                grid.GlobalUri == navigation.Uri, "Retained route content keeps its own identity when its parent or page rerenders after search navigation.");

            var searchRoute = new RouteData(typeof(SearchPage), new Dictionary<string, object?>());
            RenderFragment firstSearch = GalleryRouteView.Content(searchRoute, "examples?search=grid");
            await renderer.Render(root, firstSearch);
            SnapshotPage search = pages.Last();
            check(grid.Disposals == 1 && search.MatchedRoute == "examples?search=grid" && search.Id == "examples",
                "The new match retires the Grid owner once and supplies the complete search snapshot.");

            navigation.NavigateTo("examples?search=HDR", replace: true);
            await renderer.Render(root, firstSearch);
            search.Refresh();
            check(pages.Count == 2 && search.Disposals == 0 && search.MatchedRoute == "examples?search=grid",
                "Query navigation cannot change a retained page's matched query before Router catches up.");
            await renderer.Render(root, GalleryRouteView.Content(searchRoute, "examples?search=HDR"));
            SnapshotPage replacement = pages.Last();
            check(pages.Count == 3 && search.Disposals == 1 && replacement.MatchedRoute == "examples?search=HDR" &&
                replacement.Path == "examples", "A query-only matched change retains the path while replacing the keyed page owner.");

            navigation.NavigateTo("examples/grid-helper");
            await renderer.Render(root, GalleryRouteView.Content(gridRoute, "examples/grid-helper"));
            check(pages.Count == 4 && replacement.Disposals == 1 && pages.Last().Id == "grid-helper",
                "Returning to a disposed example creates a fresh owner with the original route identity.");
            await renderer.DisposeAsync();
            check(pages.All(page => page.Disposals == 1), "Renderer shutdown disposes every routed owner exactly once.");
        });
    }

    private sealed class ContentHost : ComponentBase
    {
        [Parameter] public RenderFragment? ChildContent { get; set; }
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, ChildContent);
    }

    private abstract class SnapshotPage(List<SnapshotPage> pages, TestNavigation navigation) : GalleryPage, IDisposable
    {
        internal string Path { get; private set; } = "";
        internal string Id { get; private set; } = "";
        internal string GlobalUri { get; private set; } = "";
        internal int Disposals { get; private set; }
        protected override void OnInitialized() => pages.Add(this);
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            Path = RoutePath; Id = RouteId; GlobalUri = navigation.Uri;
            builder.AddContent(0, Id);
        }
        internal void Refresh() => StateHasChanged();
        public void Dispose() => Disposals++;
    }

    private sealed class GridPage(List<SnapshotPage> pages, TestNavigation navigation) : SnapshotPage(pages, navigation)
    {
        protected override void OnParametersSet()
        {
            if (RouteId != "grid-helper") throw new InvalidOperationException($"Grid page received another route: {RouteId}.");
        }
    }

    private sealed class SearchPage(List<SnapshotPage> pages, TestNavigation navigation) : SnapshotPage(pages, navigation);

    private sealed class TestNavigation : NavigationManager
    {
        internal TestNavigation() => Initialize("https://example.test/GalleryAot/", "https://example.test/GalleryAot/examples/grid-helper");
        protected override void NavigateToCore(string uri, bool forceLoad) => NavigateToCore(uri, new NavigationOptions { ForceLoad = forceLoad });
        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
            Uri = ToAbsoluteUri(uri).AbsoluteUri;
            NotifyLocationChanged(false);
        }
    }

    private sealed class TestServices(TestNavigation navigation) : IServiceProvider
    {
        public object? GetService(Type type) => type == typeof(NavigationManager) ? navigation : null;
    }

    private sealed class TestActivator(TestNavigation navigation, List<SnapshotPage> pages) : IComponentActivator
    {
        public IComponent CreateInstance([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type type)
        {
            if (type == typeof(GridPage)) return new GridPage(pages, navigation);
            if (type == typeof(SearchPage)) return new SearchPage(pages, navigation);
            if (type == typeof(CascadingValue<string>)) return new CascadingValue<string>();
            if (type == typeof(RouteView)) return new RouteView();
            if (type == typeof(LayoutView)) return new LayoutView();
            throw new InvalidOperationException($"Unexpected routing component: {type}.");
        }
    }

    private sealed class RecordingRenderer(TestNavigation navigation, List<SnapshotPage> pages)
        : Renderer(new TestServices(navigation), NullLoggerFactory.Instance, new TestActivator(navigation, pages))
    {
        public override Dispatcher Dispatcher { get; } = Dispatcher.CreateDefault();
        internal int Attach(IComponent component) => AssignRootComponentId(component);
        internal Task Render(int root, RenderFragment content) => RenderRootComponentAsync(root,
            ParameterView.FromDictionary(new Dictionary<string, object?> { [nameof(ContentHost.ChildContent)] = content }));
        protected override Task UpdateDisplayAsync(in RenderBatch batch) => Task.CompletedTask;
        protected override void HandleException(Exception exception) => throw new InvalidOperationException("Gallery route rendering failed.", exception);
    }
}
#pragma warning restore BL0006
