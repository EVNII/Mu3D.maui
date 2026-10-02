using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using System.Runtime.Versioning;
using Mu3D.GalleryApp.Web;

[assembly: SupportedOSPlatform("browser")]

WebAssemblyHostBuilder builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
Mu3D.GalleryApp.Pages.GalleryAssets.Configure(new Uri(builder.HostEnvironment.BaseAddress));
await builder.Build().RunAsync();
