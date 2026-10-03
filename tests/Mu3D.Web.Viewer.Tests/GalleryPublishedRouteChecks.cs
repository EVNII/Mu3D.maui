#if !MU3D_WEB_CONTRACTS
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Mu3D.Gallery;
using Mu3D.GalleryApp.Web.Infrastructure;

// Inspect production linker output without loading the browser assembly or changing its roots.
internal static class GalleryPublishedRouteChecks
{
    internal static void Validate(string assemblyPath)
    {
        using FileStream stream = File.OpenRead(assemblyPath);
        using PEReader pe = new(stream);
        MetadataReader metadata = pe.GetMetadataReader();
        if (metadata.GetString(metadata.GetAssemblyDefinition().Name) != "Mu3D.GalleryApp.Web")
            throw new InvalidOperationException("Route retention checks require the production Gallery assembly.");

        Dictionary<string, string> routes = new(StringComparer.OrdinalIgnoreCase);
        foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
        {
            TypeDefinition type = metadata.GetTypeDefinition(handle);
            foreach (CustomAttributeHandle attributeHandle in type.GetCustomAttributes())
            {
                CustomAttribute attribute = metadata.GetCustomAttribute(attributeHandle);
                if (!IsRouteAttribute(metadata, attribute.Constructor)) continue;
                string owner = metadata.GetString(type.Namespace) + "." + metadata.GetString(type.Name);
                if ((type.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public)
                    throw new InvalidOperationException($"Published route owner is not exported: {owner}.");
                BlobReader value = metadata.GetBlobReader(attribute.Value);
                if (value.ReadUInt16() != 1 || value.ReadSerializedString() is not string route)
                    throw new InvalidOperationException($"Invalid published RouteAttribute on {owner}.");
                if (!routes.TryAdd(route, owner))
                    throw new InvalidOperationException($"Duplicate published Gallery route: {route}.");
            }
        }

        string[] expected = GalleryCatalog.Examples.Select(entry => "/" + GalleryWebCatalog.Href(entry))
            .Concat(["/", "/examples", "/sections/{SectionId}", "/source/{Feature}", "/about", "/examples/pbr-material"])
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string[] missing = expected.Where(route => !routes.ContainsKey(route)).ToArray();
        if (missing.Length != 0)
            throw new InvalidOperationException($"Production Gallery lost routes after trimming ({assemblyPath}): {string.Join(", ", missing)}.");
        Console.WriteLine($"Published Gallery route retention passed: {GalleryCatalog.Examples.Count} catalog entries, " +
            $"{expected.Length} required routes in {assemblyPath}.");
    }

    private static bool IsRouteAttribute(MetadataReader metadata, EntityHandle constructor)
    {
        EntityHandle parent = constructor.Kind switch
        {
            HandleKind.MemberReference => metadata.GetMemberReference((MemberReferenceHandle)constructor).Parent,
            HandleKind.MethodDefinition => metadata.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType(),
            _ => default,
        };
        return parent.Kind switch
        {
            HandleKind.TypeReference => IsRouteType(metadata, metadata.GetTypeReference((TypeReferenceHandle)parent)),
            HandleKind.TypeDefinition => IsRouteType(metadata, metadata.GetTypeDefinition((TypeDefinitionHandle)parent)),
            _ => false,
        };
    }

    private static bool IsRouteType(MetadataReader metadata, TypeReference type)
        => metadata.StringComparer.Equals(type.Namespace, "Microsoft.AspNetCore.Components") &&
           metadata.StringComparer.Equals(type.Name, "RouteAttribute");

    private static bool IsRouteType(MetadataReader metadata, TypeDefinition type)
        => metadata.StringComparer.Equals(type.Namespace, "Microsoft.AspNetCore.Components") &&
           metadata.StringComparer.Equals(type.Name, "RouteAttribute");
}
#endif
