namespace Mu3D.GalleryApp;

/// <summary>
/// Defines navigation for the single Mu3D Gallery sample.
/// </summary>
public partial class AppShell : Shell
{
    /// <summary>Identifies the responsive navigation mode selected by the XAML visual states.</summary>
    public static readonly BindableProperty LayoutModeProperty = BindableProperty.Create(
        nameof(LayoutMode),
        typeof(GalleryLayoutMode),
        typeof(AppShell),
        GalleryLayoutMode.BottomNavigation,
        propertyChanged: static (bindable, _, _) =>
            ((AppShell)bindable).ScheduleNavigationModeUpdate());

    private readonly ToolbarItem topExamplesItem = new()
    {
        AutomationId = "TopNavigationExamples",
        Order = ToolbarItemOrder.Primary,
        Priority = 0,
        Text = "Examples",
    };
    private readonly ToolbarItem topAdvancedItem = new()
    {
        AutomationId = "TopNavigationAdvanced",
        Order = ToolbarItemOrder.Primary,
        Priority = 1,
        Text = "Advanced",
    };
    private readonly ToolbarItem topAboutItem = new()
    {
        AutomationId = "TopNavigationAbout",
        Order = ToolbarItemOrder.Primary,
        Priority = 2,
        Text = "About",
    };
    private GalleryLayoutMode? appliedLayoutMode;
    private bool isInitialized;
    private bool isNavigationModeUpdatePending;
    private Page? topNavigationOwner;

    /// <summary>Initializes a new instance of the <see cref="AppShell"/> class.</summary>
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute(GalleryRoutes.Welcome, typeof(Pages.WelcomePage));
        Routing.RegisterRoute(GalleryRoutes.DeviceProbe, typeof(Pages.DeviceProbePage));
        Routing.RegisterRoute(GalleryRoutes.SurfaceProbe, typeof(Pages.SurfaceProbePage));
        Routing.RegisterRoute(GalleryRoutes.CustomDraw, typeof(Pages.CustomDrawPage));
        Routing.RegisterRoute(GalleryRoutes.SceneAssets, typeof(Pages.SceneAssetPage));
        Routing.RegisterRoute(GalleryRoutes.DeclarativeScene, typeof(Pages.DeclarativeScenePage));
        Routing.RegisterRoute(GalleryRoutes.DeclarativeTools, typeof(Pages.DeclarativeToolsPage));
        Routing.RegisterRoute(GalleryRoutes.OrbitControls, typeof(Pages.OrbitControlsPage));
        Routing.RegisterRoute(GalleryRoutes.MapControls, typeof(Pages.MapControlsPage));
        Routing.RegisterRoute(GalleryRoutes.FlyControls, typeof(Pages.FlyControlsPage));
        Routing.RegisterRoute(GalleryRoutes.PointerPenInput, typeof(Pages.PointerPenInputPage));
        Routing.RegisterRoute(GalleryRoutes.ProgressBridge, typeof(Pages.ProgressBridgePage));
        Routing.RegisterRoute(GalleryRoutes.PlaybackToolbar, typeof(Pages.PlaybackToolbarPage));
        Routing.RegisterRoute(GalleryRoutes.AxesHelper, typeof(Pages.AxesHelperPage));
        Routing.RegisterRoute(GalleryRoutes.GridHelper, typeof(Pages.GridHelperPage));
        Routing.RegisterRoute(GalleryRoutes.BoundsHelper, typeof(Pages.BoundsHelperPage));
        Routing.RegisterRoute(GalleryRoutes.UiAnchors, typeof(Pages.UiAnchorsPage));
        Routing.RegisterRoute(GalleryRoutes.RenderOutputs, typeof(Pages.RenderOutputsPage));
        Routing.RegisterRoute(GalleryRoutes.TransformGizmo, typeof(Pages.TransformGizmoPage));
        Routing.RegisterRoute(GalleryRoutes.FrameStatistics, typeof(Pages.FrameStatisticsPage));
        Routing.RegisterRoute(GalleryRoutes.LightingLab, typeof(Pages.LightingLabPage));
        Routing.RegisterRoute(GalleryRoutes.OcclusionLab, typeof(Pages.OcclusionLabPage));
        Routing.RegisterRoute(GalleryRoutes.MaterialTexture, typeof(Pages.MaterialTexturePage));
        Routing.RegisterRoute(GalleryRoutes.OpenPbr, typeof(Pages.OpenPbrPage));
        Routing.RegisterRoute(GalleryRoutes.OpenPbrFurnace, typeof(Pages.OpenPbrFurnacePage));
        Routing.RegisterRoute(GalleryRoutes.HdrCanvas, typeof(Pages.HdrCanvasPage));
        Routing.RegisterRoute(GalleryRoutes.ColorManagement, typeof(Pages.ColorManagementPage));
#if MU3D_PRINTING
        Routing.RegisterRoute(GalleryRoutes.CmykPrinting, typeof(Pages.CmykPrintingPage));
#endif
        Routing.RegisterRoute(GalleryRoutes.ColorLut, typeof(Pages.ColorLutPage));
        Routing.RegisterRoute(GalleryRoutes.AnimationLab, typeof(Pages.AnimationLabPage));
        Routing.RegisterRoute(GalleryRoutes.GltfLoad, typeof(Pages.GltfLoadPage));
        Routing.RegisterRoute(GalleryRoutes.GltfInstances, typeof(Pages.GltfInstancesPage));
        Routing.RegisterRoute(GalleryRoutes.GltfProductFeed, typeof(Pages.ProductFeedPage));
        Routing.RegisterRoute(GalleryRoutes.GltfAnimation, typeof(Pages.GltfAnimationPage));
        Routing.RegisterRoute(GalleryRoutes.GltfVariants, typeof(Pages.GltfVariantsPage));
        Routing.RegisterRoute(GalleryRoutes.ModelLab, typeof(Pages.ModelLabPage));
        Routing.RegisterRoute(GalleryRoutes.HdrJpegLab, typeof(Pages.HdrJpegLabPage));
        Routing.RegisterRoute(GalleryRoutes.KtxTextureLab, typeof(Pages.KtxTextureLabPage));
        Routing.RegisterRoute(GalleryRoutes.KtxDecode, typeof(Pages.KtxDecodePage));
        Routing.RegisterRoute(GalleryRoutes.Licenses, typeof(Pages.LicensesPage));

        topExamplesItem.Clicked += OnTopExamplesClicked;
        topAdvancedItem.Clicked += OnTopAdvancedClicked;
        topAboutItem.Clicked += OnTopAboutClicked;
        Navigated += OnNavigated;

        isInitialized = true;
        ApplyNavigationMode(LayoutMode);
    }

    /// <summary>Gets or sets the current navigation mode applied by the responsive XAML states.</summary>
    public GalleryLayoutMode LayoutMode
    {
        get => (GalleryLayoutMode)GetValue(LayoutModeProperty);
        set => SetValue(LayoutModeProperty, value);
    }

    internal void SelectCategory(GalleryNavigationCategory category)
    {
        NavigationRoot.CurrentItem = category switch
        {
            GalleryNavigationCategory.Examples => ExamplesTab,
            GalleryNavigationCategory.Advanced => AdvancedTab,
            GalleryNavigationCategory.About => AboutTab,
            _ => throw new ArgumentOutOfRangeException(nameof(category)),
        };
    }

    private void OnNavigated(object? sender, ShellNavigatedEventArgs e) => UpdateNavigationChrome();

    private void OnTopExamplesClicked(object? sender, EventArgs e) =>
        SelectCategory(GalleryNavigationCategory.Examples);

    private void OnTopAdvancedClicked(object? sender, EventArgs e) =>
        SelectCategory(GalleryNavigationCategory.Advanced);

    private void OnTopAboutClicked(object? sender, EventArgs e) =>
        SelectCategory(GalleryNavigationCategory.About);

    private void ScheduleNavigationModeUpdate()
    {
        if (!isInitialized || isNavigationModeUpdatePending)
        {
            return;
        }

        isNavigationModeUpdatePending = true;
        Dispatcher.Dispatch(() =>
        {
            isNavigationModeUpdatePending = false;
            ApplyNavigationMode(LayoutMode);
        });
    }

    private void ApplyNavigationMode(GalleryLayoutMode mode)
    {
        if (appliedLayoutMode == mode)
        {
            UpdateNavigationChrome();
            return;
        }

        appliedLayoutMode = mode;
        UpdateNavigationChrome();
    }

    private void UpdateNavigationChrome()
    {
        bool isTabBarVisible = appliedLayoutMode == GalleryLayoutMode.BottomNavigation;
        Shell.SetTabBarIsVisible(NavigationRoot, isTabBarVisible);

        Page? currentPage = CurrentPage;
        if (currentPage is not null)
        {
            Shell.SetTabBarIsVisible(currentPage, isTabBarVisible);
        }

        Page? newOwner = appliedLayoutMode == GalleryLayoutMode.TopNavigation
            ? currentPage
            : null;
        if (ReferenceEquals(newOwner, topNavigationOwner))
        {
            return;
        }

        if (topNavigationOwner is not null)
        {
            topNavigationOwner.ToolbarItems.Remove(topExamplesItem);
            topNavigationOwner.ToolbarItems.Remove(topAdvancedItem);
            topNavigationOwner.ToolbarItems.Remove(topAboutItem);
        }

        topNavigationOwner = newOwner;
        if (topNavigationOwner is null)
        {
            return;
        }

        topNavigationOwner.ToolbarItems.Add(topExamplesItem);
        topNavigationOwner.ToolbarItems.Add(topAdvancedItem);
        topNavigationOwner.ToolbarItems.Add(topAboutItem);
    }
}
