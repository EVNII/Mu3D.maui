using Microsoft.AspNetCore.Components;

namespace Mu3D.GalleryApp.Web.Infrastructure;

/// <summary>Reads the matched route belonging to this Gallery page, independently of later navigation.</summary>
public abstract class GalleryPage : ComponentBase
{
    /// <summary>Gets or sets the route snapshot supplied by the Gallery route view.</summary>
    [CascadingParameter(Name = nameof(MatchedRoute))]
    public string MatchedRoute { get; set; } = "";

    /// <summary>Gets the matched path without query, fragment or surrounding slashes.</summary>
    protected string RoutePath => MatchedRoute.Split('?', '#')[0].Trim('/');

    /// <summary>Gets the final path segment identifying this matched example.</summary>
    protected string RouteId => RoutePath.Split('/')[^1];
}

internal static class GalleryRouteView
{
    // Called by Router's Found fragment before the shell receives its child content.
    // Shell redraws retain this snapshot even if Navigation.Uri already points elsewhere.
    internal static RenderFragment Content(RouteData route, string relative)
    {
        string matched = relative.Split('#')[0];
        return builder =>
        {
            builder.OpenComponent<CascadingValue<string>>(0);
            builder.SetKey(matched);
            builder.AddAttribute(1, nameof(CascadingValue<string>.Name), nameof(GalleryPage.MatchedRoute));
            builder.AddAttribute(2, nameof(CascadingValue<string>.Value), matched);
            builder.AddAttribute(3, nameof(CascadingValue<string>.IsFixed), true);
            builder.AddAttribute(4, nameof(CascadingValue<string>.ChildContent), (RenderFragment)(page =>
            {
                page.OpenComponent<RouteView>(0);
                page.AddAttribute(1, nameof(RouteView.RouteData), route);
                page.CloseComponent();
            }));
            builder.CloseComponent();
        };
    }
}
