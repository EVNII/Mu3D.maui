using Android.App;
using Android.Content.PM;

namespace Mu3D.Gallery;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Android.OS.Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        using Android.Util.TypedValue material3Theme = new();
        bool attributeFound = Theme?.ResolveAttribute(Resource.Attribute.isMaterial3Theme,
            material3Theme, true) == true;
        Android.Util.Log.Info("Mu3D.Material3",
            $"Activity theme: isMaterial3Theme present={attributeFound}, " +
            $"enabled={attributeFound && material3Theme.Data != 0}");
    }
}
