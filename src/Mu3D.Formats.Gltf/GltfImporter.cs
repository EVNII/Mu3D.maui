using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using Mu3D.Assets;
using Mu3D.Color;
using Mu3D.Graphics;
using Mu3D.SceneGraph;

namespace Mu3D.Formats.Gltf;

/// <summary>Contains a backend-independent scene and animation clips imported from one glTF asset.</summary>
public sealed class GltfAsset
{
    internal GltfAsset(
        Scene scene,
        IReadOnlyList<AnimationClip> animations,
        IReadOnlyList<string> ignoredOptionalExtensions,
        IReadOnlyList<GltfMaterialVariant> materialVariants,
        IReadOnlyList<GltfMaterialVariantBinding> materialVariantBindings,
        IReadOnlyList<Material> allMaterials)
    {
        Scene = scene;
        Animations = animations;
        IgnoredOptionalExtensions = ignoredOptionalExtensions;
        MaterialVariants = materialVariants;
        this.materialVariantBindings = materialVariantBindings;
        this.allMaterials = allMaterials;
        MaterialShaderVariants = MaterialVariantManifest.FromMaterials(allMaterials);
    }

    /// <summary>Gets the imported default scene.</summary>
    public Scene Scene { get; }

    /// <summary>Gets imported animation clips in source order.</summary>
    public IReadOnlyList<AnimationClip> Animations { get; }

    /// <summary>
    /// Gets the de-duplicated shader variants required by every mesh in the imported default scene.
    /// A host may prewarm these variants before presenting the first frame, including under AOT.
    /// </summary>
    public MaterialVariantManifest MaterialShaderVariants { get; }

    /// <summary>Gets authored KHR_materials_variants entries in source order.</summary>
    public IReadOnlyList<GltfMaterialVariant> MaterialVariants { get; }

    /// <summary>
    /// Gets optional exact encoded source bytes retained by the asynchronous loader. Decoded or
    /// transcoded texture data referenced by materials is independent of this archive.
    /// </summary>
    public GltfSourceArchive? SourceArchive { get; private set; }

    /// <summary>Gets the active authored material-variant index, or null for default glTF materials.</summary>
    public int? ActiveMaterialVariantIndex { get; private set; }

    /// <summary>
    /// Captures this import as a reusable definition that can create independent scene instances.
    /// Geometry and texture sources remain shared while mutable nodes, materials and animation
    /// targets are cloned for each instance.
    /// </summary>
    /// <param name="name">Optional reusable definition name.</param>
    public GltfSceneAsset CreateSceneAsset(string? name = null) => new(this, name);

    /// <summary>Applies one authored material variant, or restores defaults when index is null.</summary>
    public void ApplyMaterialVariant(int? index)
    {
        if (index.HasValue && (uint)index.Value >= (uint)MaterialVariants.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        foreach (GltfMaterialVariantBinding binding in materialVariantBindings)
        {
            binding.Mesh.Material = index.HasValue &&
                binding.Materials.TryGetValue(index.Value, out Material? material)
                    ? material
                    : binding.DefaultMaterial;
        }
        ActiveMaterialVariantIndex = index;
    }

    /// <summary>Applies the uniquely named authored material variant.</summary>
    public void ApplyMaterialVariant(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        int index = -1;
        for (int candidate = 0; candidate < MaterialVariants.Count; candidate++)
        {
            if (!string.Equals(MaterialVariants[candidate].Name, name, StringComparison.Ordinal))
            {
                continue;
            }
            if (index >= 0)
            {
                throw new InvalidOperationException($"Material variant name '{name}' is not unique.");
            }
            index = candidate;
        }
        if (index < 0)
        {
            throw new KeyNotFoundException($"Material variant '{name}' was not found.");
        }
        ApplyMaterialVariant(index);
    }

    /// <summary>
    /// Gets optional source extensions whose semantics were not applied. Their core glTF fallback
    /// remains imported; required unsupported extensions still fail the entire import.
    /// </summary>
    public IReadOnlyList<string> IgnoredOptionalExtensions { get; }

    internal void AttachSourceArchive(GltfSourceArchive sourceArchive)
    {
        ArgumentNullException.ThrowIfNull(sourceArchive);
        if (SourceArchive is not null)
        {
            throw new InvalidOperationException("A glTF source archive is already attached.");
        }
        SourceArchive = sourceArchive;
    }

    private readonly IReadOnlyList<GltfMaterialVariantBinding> materialVariantBindings;
    private readonly IReadOnlyList<Material> allMaterials;

    internal IReadOnlyList<GltfMaterialVariantBinding> MaterialVariantBindings =>
        materialVariantBindings;

    internal IReadOnlyList<Material> AllMaterials => allMaterials;
}

/// <summary>Names one asset-wide KHR_materials_variants selection.</summary>
public sealed record GltfMaterialVariant
{
    internal GltfMaterialVariant(int index, string name)
    {
        Index = index;
        Name = name;
    }

    /// <summary>Gets the zero-based source variant index.</summary>
    public int Index { get; }

    /// <summary>Gets the authored display name.</summary>
    public string Name { get; }
}

internal sealed record GltfMaterialVariantBinding(
    Mesh Mesh,
    Material DefaultMaterial,
    IReadOnlyDictionary<int, Material> Materials);

/// <summary>
/// Imports a fail-closed portable subset of glTF 2.0, including triangle meshes, scalar PBR
/// materials, color and non-color raster maps, alpha modes, skinning, transform animation,
/// morph targets and morph-weight animation, into Mu3D scene-domain objects.
/// </summary>
public static class GltfImporter
{
    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunk = 0x4E4F534A;
    private const uint BinaryChunk = 0x004E4942;
    private const string BasisTextureExtension = "KHR_texture_basisu";
    private const string EmissiveStrengthExtension = "KHR_materials_emissive_strength";
    private const string ClearcoatExtension = "KHR_materials_clearcoat";
    private const string AnisotropyExtension = "KHR_materials_anisotropy";
    private const string TransmissionExtension = "KHR_materials_transmission";
    private const string VolumeExtension = "KHR_materials_volume";
    private const string IorExtension = "KHR_materials_ior";
    private const string DispersionExtension = "KHR_materials_dispersion";
    private const string SheenExtension = "KHR_materials_sheen";
    private const string IridescenceExtension = "KHR_materials_iridescence";
    private const string SpecularExtension = "KHR_materials_specular";
    private const string DiffuseTransmissionExtension = "KHR_materials_diffuse_transmission";
    private const string UnlitExtension = "KHR_materials_unlit";
    private const string PunctualLightsExtension = "KHR_lights_punctual";
    private const string TextureTransformExtension = "KHR_texture_transform";
    private const string MaterialVariantsExtension = "KHR_materials_variants";

    /// <summary>
    /// Imports JSON glTF or binary GLB bytes. External buffer URIs are resolved only through the
    /// supplied callback; data URIs and the GLB binary chunk require no callback.
    /// </summary>
    /// <param name="source">Complete UTF-8 glTF JSON or GLB bytes.</param>
    /// <param name="externalBufferResolver">Optional resolver for non-data buffer URIs.</param>
    /// <param name="name">Optional imported scene name override.</param>
    /// <param name="imageDecoder">
    /// Optional decoder for encoded image formats outside the built-in PNG color path.
    /// </param>
    /// <returns>A backend-independent scene containing supported triangle meshes and PBR materials.</returns>
    public static Scene Import(
        ReadOnlyMemory<byte> source,
        Func<string, ReadOnlyMemory<byte>>? externalBufferResolver = null,
        string? name = null,
        IEncodedImageDecoder? imageDecoder = null) =>
        ImportAsset(source, externalBufferResolver, name, imageDecoder).Scene;

    /// <summary>Imports JSON glTF or binary GLB bytes using explicit application services.</summary>
    /// <param name="source">Complete UTF-8 glTF JSON or GLB bytes.</param>
    /// <param name="options">Import services and destination capabilities.</param>
    /// <returns>A backend-independent scene containing supported triangle meshes and PBR materials.</returns>
    public static Scene ImportWithOptions(
        ReadOnlyMemory<byte> source,
        GltfImportOptions options) => ImportAssetWithOptions(source, options).Scene;

    /// <summary>Imports a glTF/GLB scene together with its supported animation clips.</summary>
    /// <param name="source">Complete UTF-8 glTF JSON or GLB bytes.</param>
    /// <param name="externalBufferResolver">Optional resolver for non-data buffer URIs.</param>
    /// <param name="name">Optional imported scene name override.</param>
    /// <param name="imageDecoder">Optional decoder for JPEG and other encoded raster maps.</param>
    /// <returns>A backend-independent scene and its supported animation clips.</returns>
    public static GltfAsset ImportAsset(
        ReadOnlyMemory<byte> source,
        Func<string, ReadOnlyMemory<byte>>? externalBufferResolver = null,
        string? name = null,
        IEncodedImageDecoder? imageDecoder = null)
        => ImportAssetWithOptions(
            source,
            new GltfImportOptions
            {
                ExternalBufferResolver = externalBufferResolver,
                Name = name,
                ImageDecoder = imageDecoder,
            });

    /// <summary>Imports a glTF/GLB scene and animations using explicit application services.</summary>
    /// <param name="source">Complete UTF-8 glTF JSON or GLB bytes.</param>
    /// <param name="options">Import services and destination capabilities.</param>
    /// <returns>A backend-independent scene and its supported animation clips.</returns>
    public static GltfAsset ImportAssetWithOptions(
        ReadOnlyMemory<byte> source,
        GltfImportOptions options)
        => ImportAssetCore(source, options, CancellationToken.None);

    internal static GltfAsset ImportAssetCore(
        ReadOnlyMemory<byte> source,
        GltfImportOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (source.IsEmpty)
        {
            throw new ArgumentException("glTF input cannot be empty.", nameof(source));
        }

        cancellationToken.ThrowIfCancellationRequested();
        (ReadOnlyMemory<byte> json, ReadOnlyMemory<byte>? binaryChunk) = SplitContainer(source);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        ValidateAsset(root);
        if (options.TextureTranscoder is not null && options.GraphicsCapabilities is null)
        {
            throw new ArgumentException(
                "A texture transcoder requires explicit destination graphics capabilities.",
                nameof(options));
        }
        cancellationToken.ThrowIfCancellationRequested();
        ReadOnlyMemory<byte>[] buffers = ReadBuffers(
            root,
            binaryChunk,
            options.ExternalBufferResolver);
        ImportContext context = new(root, buffers);
        cancellationToken.ThrowIfCancellationRequested();
        EncodedImageSet images = ReadImages(
            context,
            options.ExternalImageResolver,
            options.ImageDecoder,
            options.TextureTranscoder,
            options.GraphicsCapabilities);
        cancellationToken.ThrowIfCancellationRequested();
        Material[] materials = ReadMaterials(context, images);
        cancellationToken.ThrowIfCancellationRequested();
        GltfMaterialVariant[] materialVariants = ReadMaterialVariants(root);
        MeshDefinition[] meshDefinitions = ReadMeshes(context, materialVariants.Length);
        cancellationToken.ThrowIfCancellationRequested();
        PunctualLightDefinition[] punctualLights = ReadPunctualLights(root);
        NodeImport nodes = ReadNodes(root, meshDefinitions, materials, punctualLights);
        cancellationToken.ThrowIfCancellationRequested();
        AttachNodeHierarchy(root, nodes.Nodes);
        AttachSkins(context, nodes);

        int sceneIndex = root.TryGetProperty("scene", out JsonElement sceneElement)
            ? ReadIndex(sceneElement, "scene")
            : 0;
        JsonElement scenes = RequireArray(root, "scenes");
        if ((uint)sceneIndex >= (uint)scenes.GetArrayLength())
        {
            throw new InvalidDataException("The default glTF scene index is out of range.");
        }
        JsonElement selectedScene = scenes[sceneIndex];
        Scene result = new(options.Name ?? ReadOptionalString(selectedScene, "name"));
        if (selectedScene.TryGetProperty("nodes", out JsonElement roots))
        {
            foreach (JsonElement nodeIndexElement in AsArray(roots, "scene.nodes").EnumerateArray())
            {
                int nodeIndex = ReadIndex(nodeIndexElement, "scene node");
                SceneNode node = At(nodes.Nodes, nodeIndex, "scene node");
                if (node.Parent is not null)
                {
                    throw new InvalidDataException("A glTF scene root is already parented by another node.");
                }
                result.Add(node);
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
        AnimationClip[] animations = ReadAnimations(context, nodes);
        cancellationToken.ThrowIfCancellationRequested();
        return new GltfAsset(
            result,
            Array.AsReadOnly(animations),
            Array.AsReadOnly(context.IgnoredOptionalExtensions.Order(StringComparer.Ordinal).ToArray()),
            Array.AsReadOnly(materialVariants),
            Array.AsReadOnly(nodes.MaterialVariantBindings),
            Array.AsReadOnly(materials));
    }

    internal static (ReadOnlyMemory<byte> Json, ReadOnlyMemory<byte>? Binary) SplitContainer(
        ReadOnlyMemory<byte> source)
    {
        ReadOnlySpan<byte> bytes = source.Span;
        if (bytes.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian(bytes) != GlbMagic)
        {
            return (source, null);
        }
        if (bytes.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]) != 2)
        {
            throw new InvalidDataException("GLB must use version 2 and contain a JSON chunk.");
        }
        uint declaredLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        if (declaredLength != bytes.Length)
        {
            throw new InvalidDataException("GLB declared length does not match its byte length.");
        }
        int offset = 12;
        ReadOnlyMemory<byte>? json = null;
        ReadOnlyMemory<byte>? binary = null;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 8)
            {
                throw new InvalidDataException("GLB has a truncated chunk header.");
            }
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset)..]);
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(offset + 4)..]);
            offset += 8;
            if (length > int.MaxValue || offset > bytes.Length - (int)length)
            {
                throw new InvalidDataException("GLB has a truncated chunk payload.");
            }
            ReadOnlyMemory<byte> payload = source.Slice(offset, (int)length);
            if (type == JsonChunk && json is null)
            {
                json = payload;
            }
            else if (type == BinaryChunk && binary is null)
            {
                binary = payload;
            }
            offset += (int)length;
        }
        return (json ?? throw new InvalidDataException("GLB does not contain a JSON chunk."), binary);
    }

    internal static IReadOnlyList<string> ReadExternalResourceUris(ReadOnlyMemory<byte> source)
    {
        (ReadOnlyMemory<byte> json, _) = SplitContainer(source);
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        List<string> result = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        CollectExternalResourceUris(root, "buffers", result, seen);
        CollectExternalResourceUris(root, "images", result, seen);
        return result;
    }

    private static void CollectExternalResourceUris(
        JsonElement root,
        string propertyName,
        List<string> result,
        HashSet<string> seen)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement resources))
        {
            return;
        }

        foreach (JsonElement resource in AsArray(resources, propertyName).EnumerateArray())
        {
            if (resource.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"glTF {propertyName} entries must be objects.");
            }
            if (!resource.TryGetProperty("uri", out JsonElement uriElement))
            {
                continue;
            }
            if (uriElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"glTF {propertyName} URI must be a string.");
            }
            string uri = uriElement.GetString()!;
            if (!uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && seen.Add(uri))
            {
                result.Add(uri);
            }
        }
    }

    private static void ValidateAsset(JsonElement root)
    {
        JsonElement asset = RequireObject(root, "asset");
        if (ReadOptionalString(asset, "version") != "2.0")
        {
            throw new NotSupportedException("Only glTF 2.0 is supported.");
        }
        if (root.TryGetProperty("extensionsRequired", out JsonElement required))
        {
            foreach (JsonElement extension in AsArray(required, "extensionsRequired").EnumerateArray())
            {
                if (extension.ValueKind != JsonValueKind.String ||
                    extension.GetString() is not (
                        BasisTextureExtension or
                        EmissiveStrengthExtension or
                        ClearcoatExtension or AnisotropyExtension or TransmissionExtension or
                        VolumeExtension or IorExtension or
                        DispersionExtension or
                        SheenExtension or IridescenceExtension or SpecularExtension or
                        DiffuseTransmissionExtension or
                        UnlitExtension or
                        PunctualLightsExtension or
                        TextureTransformExtension or
                        MaterialVariantsExtension))
                {
                    throw new NotSupportedException(
                        $"Required glTF extension '{extension}' is not supported by this slice.");
                }
            }
        }
    }

    private static ReadOnlyMemory<byte>[] ReadBuffers(
        JsonElement root,
        ReadOnlyMemory<byte>? binaryChunk,
        Func<string, ReadOnlyMemory<byte>>? resolver)
    {
        JsonElement array = RequireArray(root, "buffers");
        ReadOnlyMemory<byte>[] result = new ReadOnlyMemory<byte>[array.GetArrayLength()];
        for (int index = 0; index < result.Length; index++)
        {
            JsonElement buffer = array[index];
            int byteLength = ReadNonNegativeInt(RequireProperty(buffer, "byteLength"), "buffer.byteLength");
            ReadOnlyMemory<byte> data;
            if (buffer.TryGetProperty("uri", out JsonElement uriElement))
            {
                string uri = uriElement.GetString() ?? throw new InvalidDataException("Buffer URI must be a string.");
                data = uri.StartsWith("data:", StringComparison.Ordinal)
                    ? DecodeDataUri(uri)
                    : resolver?.Invoke(uri) ?? throw new InvalidDataException(
                        $"External buffer '{uri}' requires an external buffer resolver.");
            }
            else if (index == 0 && binaryChunk is not null)
            {
                data = binaryChunk.Value;
            }
            else
            {
                throw new InvalidDataException("A buffer has neither a URI nor a matching GLB binary chunk.");
            }
            if (data.Length < byteLength)
            {
                throw new InvalidDataException("Resolved buffer is shorter than its declared byteLength.");
            }
            result[index] = data[..byteLength];
        }
        return result;
    }

    private static ReadOnlyMemory<byte> DecodeDataUri(string uri)
    {
        int comma = uri.IndexOf(',');
        if (comma < 0 || !uri[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("Only base64 glTF data URIs are supported.");
        }
        try
        {
            return Convert.FromBase64String(uri[(comma + 1)..]);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("A glTF data URI contains invalid base64.", exception);
        }
    }

    private static EncodedImageSet ReadImages(
        ImportContext context,
        Func<string, ReadOnlyMemory<byte>>? externalImageResolver,
        IEncodedImageDecoder? decoder,
        IEncodedTextureTranscoder? transcoder,
        GraphicsCapabilities? capabilities)
    {
        if (!context.Root.TryGetProperty("images", out JsonElement imageArray))
        {
            return new EncodedImageSet([], decoder, transcoder, capabilities);
        }
        JsonElement images = AsArray(imageArray, "images");
        EncodedImage[] result = new EncodedImage[images.GetArrayLength()];
        for (int index = 0; index < result.Length; index++)
        {
            JsonElement image = images[index];
            string? mimeType = ReadOptionalString(image, "mimeType");
            ReadOnlyMemory<byte> encoded;
            if (image.TryGetProperty("bufferView", out JsonElement viewElement))
            {
                mimeType = NormalizeImageMimeType(mimeType);
                encoded = context.ReadBufferView(ReadIndex(viewElement, "image.bufferView"));
            }
            else if (image.TryGetProperty("uri", out JsonElement uriElement))
            {
                string uri = uriElement.GetString() ??
                    throw new InvalidDataException("Image URI must be a string.");
                if (!uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    if (externalImageResolver is null)
                    {
                        throw new InvalidDataException(
                            $"External glTF image '{uri}' requires an explicit image resolver.");
                    }
                    encoded = externalImageResolver(uri);
                    if (encoded.IsEmpty)
                    {
                        throw new InvalidDataException(
                            $"The external glTF image resolver returned no bytes for '{uri}'.");
                    }
                    mimeType = NormalizeImageMimeType(mimeType ?? InferImageMimeType(uri));
                    result[index] = new EncodedImage(
                        encoded,
                        mimeType,
                        ReadOptionalString(image, "name"));
                    continue;
                }
                string uriMimeType = NormalizeImageMimeType(ReadDataUriMimeType(uri));
                if (mimeType is not null &&
                    !string.Equals(mimeType, uriMimeType, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "A glTF image MIME type does not match its data URI media type.");
                }
                mimeType = uriMimeType;
                encoded = DecodeDataUri(uri);
            }
            else
            {
                throw new InvalidDataException("A glTF image requires a bufferView or URI.");
            }
            result[index] = new EncodedImage(
                encoded,
                mimeType ?? throw new InvalidDataException("A glTF image requires a MIME type."),
                ReadOptionalString(image, "name"));
        }
        return new EncodedImageSet(result, decoder, transcoder, capabilities);
    }

    private static string ReadDataUriMimeType(string uri)
    {
        int comma = uri.IndexOf(',');
        int semicolon = uri.IndexOf(';');
        int end = semicolon >= 0 && semicolon < comma ? semicolon : comma;
        if (comma < 0 || end <= 5)
        {
            throw new InvalidDataException("A glTF image data URI requires an explicit media type.");
        }
        return uri[5..end];
    }

    private static string NormalizeImageMimeType(string? mimeType) =>
        mimeType?.ToLowerInvariant() switch
        {
            "image/png" => "image/png",
            "image/jpeg" => "image/jpeg",
            "image/ktx2" => "image/ktx2",
            null => throw new InvalidDataException("An embedded glTF image requires a MIME type."),
            _ => throw new NotSupportedException(
                $"glTF image MIME type '{mimeType}' is not supported by this slice."),
        };

    private static string InferImageMimeType(string uri)
    {
        string path = uri;
        int queryOrFragment = path.IndexOfAny(['?', '#']);
        if (queryOrFragment >= 0)
        {
            path = path[..queryOrFragment];
        }
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".ktx2" => "image/ktx2",
            _ => throw new InvalidDataException(
                $"External glTF image '{uri}' requires an explicit supported MIME type."),
        };
    }

    private static Material[] ReadMaterials(ImportContext context, EncodedImageSet images)
    {
        JsonElement root = context.Root;
        if (!root.TryGetProperty("materials", out JsonElement materialArray))
        {
            return [];
        }
        JsonElement array = AsArray(materialArray, "materials");
        Material[] materials = new Material[array.GetArrayLength()];
        for (int index = 0; index < materials.Length; index++)
        {
            JsonElement source = array[index];
            JsonElement? emissiveStrengthExtension = null;
            JsonElement? clearcoatExtension = null;
            JsonElement? anisotropyExtension = null;
            JsonElement? transmissionExtension = null;
            JsonElement? volumeExtension = null;
            JsonElement? iorExtension = null;
            JsonElement? dispersionExtension = null;
            JsonElement? sheenExtension = null;
            JsonElement? iridescenceExtension = null;
            JsonElement? specularExtension = null;
            JsonElement? diffuseTransmissionExtension = null;
            bool isUnlit = false;
            if (source.TryGetProperty("extensions", out JsonElement materialExtensions))
            {
                if (materialExtensions.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException("glTF material extensions must be an object.");
                }
                foreach (JsonProperty extension in materialExtensions.EnumerateObject())
                {
                    if (extension.Name == BasisTextureExtension)
                    {
                        throw new InvalidDataException(
                            $"{BasisTextureExtension} is a texture extension, not a material extension.");
                    }
                    if (extension.Name == EmissiveStrengthExtension)
                    {
                        if (extension.Value.ValueKind != JsonValueKind.Object)
                        {
                            throw new InvalidDataException(
                                $"glTF {EmissiveStrengthExtension} must be an object.");
                        }
                        emissiveStrengthExtension = extension.Value;
                        continue;
                    }
                    if (extension.Name == ClearcoatExtension)
                    {
                        if (extension.Value.ValueKind != JsonValueKind.Object)
                        {
                            throw new InvalidDataException(
                                $"glTF {ClearcoatExtension} must be an object.");
                        }
                        clearcoatExtension = extension.Value;
                        continue;
                    }
                    if (extension.Name == AnisotropyExtension)
                    {
                        if (extension.Value.ValueKind != JsonValueKind.Object)
                        {
                            throw new InvalidDataException(
                                $"glTF {AnisotropyExtension} must be an object.");
                        }
                        anisotropyExtension = extension.Value;
                        continue;
                    }
                    if (extension.Name is TransmissionExtension or VolumeExtension or IorExtension or
                        DispersionExtension)
                    {
                        if (extension.Value.ValueKind != JsonValueKind.Object)
                        {
                            throw new InvalidDataException($"glTF {extension.Name} must be an object.");
                        }
                        if (extension.Name == TransmissionExtension) transmissionExtension = extension.Value;
                        else if (extension.Name == VolumeExtension) volumeExtension = extension.Value;
                        else if (extension.Name == IorExtension) iorExtension = extension.Value;
                        else dispersionExtension = extension.Value;
                        continue;
                    }
                    if (extension.Name == SheenExtension)
                    {
                        if (extension.Value.ValueKind != JsonValueKind.Object)
                        {
                            throw new InvalidDataException($"glTF {SheenExtension} must be an object.");
                        }
                        sheenExtension = extension.Value;
                        continue;
                    }
                    if (extension.Name == IridescenceExtension)
                    {
                        if (extension.Value.ValueKind != JsonValueKind.Object)
                        {
                            throw new InvalidDataException($"glTF {IridescenceExtension} must be an object.");
                        }
                        iridescenceExtension = extension.Value;
                        continue;
                    }
                    if (extension.Name == SpecularExtension)
                    {
                        if (extension.Value.ValueKind != JsonValueKind.Object)
                        {
                            throw new InvalidDataException($"glTF {SpecularExtension} must be an object.");
                        }
                        specularExtension = extension.Value;
                        continue;
                    }
                    if (extension.Name == DiffuseTransmissionExtension)
                    {
                        if (extension.Value.ValueKind != JsonValueKind.Object)
                        {
                            throw new InvalidDataException(
                                $"glTF {DiffuseTransmissionExtension} must be an object.");
                        }
                        diffuseTransmissionExtension = extension.Value;
                        continue;
                    }
                    if (extension.Name == UnlitExtension)
                    {
                        if (extension.Value.ValueKind != JsonValueKind.Object)
                        {
                            throw new InvalidDataException($"glTF {UnlitExtension} must be an object.");
                        }
                        isUnlit = true;
                        continue;
                    }
                    context.RecordIgnoredOptionalExtension(extension.Name);
                }
                if (emissiveStrengthExtension.HasValue &&
                    isUnlit)
                {
                    throw new InvalidDataException(
                        $"glTF {EmissiveStrengthExtension} must not be combined with {UnlitExtension}.");
                }
                if (clearcoatExtension.HasValue &&
                    isUnlit)
                {
                    throw new InvalidDataException(
                        $"glTF {ClearcoatExtension} must not be combined with {UnlitExtension}.");
                }
                if (anisotropyExtension.HasValue &&
                    isUnlit)
                {
                    throw new InvalidDataException(
                        $"glTF {AnisotropyExtension} must not be combined with {UnlitExtension}.");
                }
                if ((transmissionExtension.HasValue || volumeExtension.HasValue || iorExtension.HasValue ||
                     dispersionExtension.HasValue) &&
                    isUnlit)
                {
                    throw new InvalidDataException(
                        $"glTF transmission, volume and IOR extensions must not be combined with {UnlitExtension}.");
                }
                if (sheenExtension.HasValue && isUnlit)
                {
                    throw new InvalidDataException($"glTF {SheenExtension} must not be combined with {UnlitExtension}.");
                }
                if (iridescenceExtension.HasValue &&
                    isUnlit)
                {
                    throw new InvalidDataException(
                        $"glTF {IridescenceExtension} must not be combined with {UnlitExtension}.");
                }
                if (specularExtension.HasValue &&
                    isUnlit)
                {
                    throw new InvalidDataException(
                        $"glTF {SpecularExtension} must not be combined with {UnlitExtension}.");
                }
                if (diffuseTransmissionExtension.HasValue &&
                    isUnlit)
                {
                    throw new InvalidDataException(
                        $"glTF {DiffuseTransmissionExtension} must not be combined with {UnlitExtension}.");
                }
            }
            JsonElement? pbr = source.TryGetProperty("pbrMetallicRoughness", out JsonElement pbrElement)
                ? pbrElement
                : null;
            Vector4 factor = pbr is JsonElement p && p.TryGetProperty("baseColorFactor", out JsonElement color)
                ? ReadUnitVector4(color, "baseColorFactor")
                : Vector4.One;
            if (isUnlit)
            {
                UnlitMaterial unlit = new(
                    new LinearRgba(factor.X, factor.Y, factor.Z, factor.W, StandardColorSpaces.LinearSrgb),
                    ReadOptionalString(source, "name"))
                {
                    IsDoubleSided = ReadOptionalBoolean(source, "doubleSided"),
                };
                if (pbr is JsonElement unlitPbr &&
                    unlitPbr.TryGetProperty("baseColorTexture", out JsonElement unlitTextureInfo))
                {
                    TextureReference texture = ReadTextureReference(
                        context.Root,
                        unlitTextureInfo,
                        "baseColorTexture");
                    ColorTextureSource decoded = images.DecodeColor(texture.ImageIndex);
                    unlit.BaseColorTexture = decoded.Decoded;
                    unlit.CompressedBaseColorTexture = decoded.Compressed;
                    unlit.BaseColorTextureMapping = texture.Mapping;
                }
                string unlitAlphaMode = ReadOptionalString(source, "alphaMode") ?? "OPAQUE";
                unlit.AlphaMode = unlitAlphaMode switch
                {
                    "OPAQUE" => MaterialAlphaMode.Opaque,
                    "MASK" => MaterialAlphaMode.Mask,
                    "BLEND" => MaterialAlphaMode.Blend,
                    _ => throw new InvalidDataException($"Unknown glTF alphaMode '{unlitAlphaMode}'."),
                };
                if (source.TryGetProperty("alphaCutoff", out JsonElement unlitCutoff))
                {
                    unlit.AlphaCutoff = ReadUnitFloat(unlitCutoff, "alphaCutoff");
                }
                materials[index] = unlit;
                continue;
            }
            float metallic = pbr is JsonElement pbrValue &&
                pbrValue.TryGetProperty("metallicFactor", out JsonElement metallicElement)
                    ? ReadUnitFloat(metallicElement, "metallicFactor")
                    : 1f;
            float roughness = pbr is JsonElement pbrValue2 &&
                pbrValue2.TryGetProperty("roughnessFactor", out JsonElement roughnessElement)
                    ? ReadUnitFloat(roughnessElement, "roughnessFactor")
                    : 1f;
            PbrMaterial material = new(
                new LinearRgba(factor.X, factor.Y, factor.Z, factor.W, StandardColorSpaces.LinearSrgb),
                metallic,
                roughness,
                ReadOptionalString(source, "name"))
            {
                IsDoubleSided = ReadOptionalBoolean(source, "doubleSided"),
            };
            if (emissiveStrengthExtension is JsonElement strengthExtension &&
                strengthExtension.TryGetProperty(
                    "emissiveStrength",
                    out JsonElement emissiveStrengthElement))
            {
                material.EmissiveStrength = ReadNonNegativeFloat(
                    emissiveStrengthElement,
                    $"{EmissiveStrengthExtension}.emissiveStrength");
            }
            if (source.TryGetProperty("emissiveFactor", out JsonElement emissiveElement))
            {
                Vector3 emissive = ReadUnitVector3(emissiveElement, "emissiveFactor");
                material.EmissiveColor = new LinearRgba(
                    emissive.X,
                    emissive.Y,
                    emissive.Z,
                    1f,
                    StandardColorSpaces.LinearSrgb);
            }
            if (clearcoatExtension is JsonElement clearcoat)
            {
                material.ClearcoatFactor = clearcoat.TryGetProperty(
                    "clearcoatFactor",
                    out JsonElement clearcoatFactor)
                    ? ReadUnitFloat(clearcoatFactor, $"{ClearcoatExtension}.clearcoatFactor")
                    : 0f;
                material.ClearcoatRoughness = clearcoat.TryGetProperty(
                    "clearcoatRoughnessFactor",
                    out JsonElement clearcoatRoughness)
                    ? ReadUnitFloat(
                        clearcoatRoughness,
                        $"{ClearcoatExtension}.clearcoatRoughnessFactor")
                    : 0f;
                if (clearcoat.TryGetProperty("clearcoatTexture", out JsonElement clearcoatInfo))
                {
                    TextureReference texture = ReadTextureReference(
                        context.Root,
                        clearcoatInfo,
                        $"{ClearcoatExtension}.clearcoatTexture");
                    DataTextureSource decoded = images.DecodeData(texture.ImageIndex);
                    material.ClearcoatTexture = decoded.Decoded;
                    material.CompressedClearcoatTexture = decoded.Compressed;
                    material.ClearcoatTextureMapping = texture.Mapping;
                }
                if (clearcoat.TryGetProperty(
                    "clearcoatRoughnessTexture",
                    out JsonElement roughnessInfo))
                {
                    TextureReference texture = ReadTextureReference(
                        context.Root,
                        roughnessInfo,
                        $"{ClearcoatExtension}.clearcoatRoughnessTexture");
                    DataTextureSource decoded = images.DecodeData(texture.ImageIndex);
                    material.ClearcoatRoughnessTexture = decoded.Decoded;
                    material.CompressedClearcoatRoughnessTexture = decoded.Compressed;
                    material.ClearcoatRoughnessTextureMapping = texture.Mapping;
                }
                if (clearcoat.TryGetProperty(
                    "clearcoatNormalTexture",
                    out JsonElement clearcoatNormalInfo))
                {
                    TextureReference texture = ReadTextureReference(
                        context.Root,
                        clearcoatNormalInfo,
                        $"{ClearcoatExtension}.clearcoatNormalTexture");
                    DataTextureSource decoded = images.DecodeData(texture.ImageIndex);
                    material.ClearcoatNormalTexture = decoded.Decoded;
                    material.CompressedClearcoatNormalTexture = decoded.Compressed;
                    material.ClearcoatNormalScale = clearcoatNormalInfo.TryGetProperty(
                        "scale",
                        out JsonElement clearcoatScale)
                        ? ReadNonNegativeFloat(
                            clearcoatScale,
                            $"{ClearcoatExtension}.clearcoatNormalTexture.scale")
                        : 1f;
                    material.ClearcoatNormalTextureMapping = texture.Mapping;
                }
            }
            if (anisotropyExtension is JsonElement anisotropy)
            {
                material.AnisotropyStrength = anisotropy.TryGetProperty(
                    "anisotropyStrength", out JsonElement strength)
                    ? ReadUnitFloat(strength, $"{AnisotropyExtension}.anisotropyStrength")
                    : 0f;
                material.AnisotropyRotation = anisotropy.TryGetProperty(
                    "anisotropyRotation", out JsonElement rotation)
                    ? ReadFiniteFloat(rotation, $"{AnisotropyExtension}.anisotropyRotation")
                    : 0f;
                if (anisotropy.TryGetProperty("anisotropyTexture", out JsonElement anisotropyInfo))
                {
                    TextureReference texture = ReadTextureReference(
                        context.Root, anisotropyInfo, $"{AnisotropyExtension}.anisotropyTexture");
                    DataTextureSource decoded = images.DecodeData(texture.ImageIndex);
                    material.AnisotropyTexture = decoded.Decoded;
                    material.CompressedAnisotropyTexture = decoded.Compressed;
                    material.AnisotropyTextureMapping = texture.Mapping;
                }
            }
            if (transmissionExtension is JsonElement transmission)
            {
                material.TransmissionFactor = transmission.TryGetProperty(
                    "transmissionFactor", out JsonElement factorElement)
                    ? ReadUnitFloat(factorElement, $"{TransmissionExtension}.transmissionFactor")
                    : 0f;
                if (transmission.TryGetProperty(
                    "transmissionTexture",
                    out JsonElement transmissionTextureInfo))
                {
                    TextureReference texture = ReadTextureReference(
                        context.Root,
                        transmissionTextureInfo,
                        $"{TransmissionExtension}.transmissionTexture");
                    DataTextureSource decoded = images.DecodeData(texture.ImageIndex);
                    material.TransmissionTexture = decoded.Decoded;
                    material.CompressedTransmissionTexture = decoded.Compressed;
                    material.TransmissionTextureMapping = texture.Mapping;
                }
            }
            if (iorExtension is JsonElement ior &&
                ior.TryGetProperty("ior", out JsonElement iorElement))
            {
                float value = ReadFiniteFloat(iorElement, $"{IorExtension}.ior");
                if (value != 0f && value < 1f)
                {
                    throw new InvalidDataException(
                        $"glTF '{IorExtension}.ior' must be zero or at least one.");
                }
                material.IndexOfRefraction = value;
            }
            if (dispersionExtension is JsonElement dispersion)
            {
                material.Dispersion = dispersion.TryGetProperty(
                    "dispersion", out JsonElement dispersionElement)
                    ? ReadNonNegativeFloat(
                        dispersionElement,
                        $"{DispersionExtension}.dispersion")
                    : 0f;
            }
            if (volumeExtension is JsonElement volume)
            {
                material.VolumeThicknessFactor = volume.TryGetProperty(
                    "thicknessFactor", out JsonElement thicknessElement)
                    ? ReadNonNegativeFloat(thicknessElement, $"{VolumeExtension}.thicknessFactor")
                    : 0f;
                if (volume.TryGetProperty(
                    "thicknessTexture",
                    out JsonElement thicknessTextureInfo))
                {
                    TextureReference texture = ReadTextureReference(
                        context.Root,
                        thicknessTextureInfo,
                        $"{VolumeExtension}.thicknessTexture");
                    DataTextureSource decoded = images.DecodeData(texture.ImageIndex);
                    material.VolumeThicknessTexture = decoded.Decoded;
                    material.CompressedVolumeThicknessTexture = decoded.Compressed;
                    material.VolumeThicknessTextureMapping = texture.Mapping;
                }
                if (volume.TryGetProperty("attenuationDistance", out JsonElement distanceElement))
                {
                    float distance = ReadFiniteFloat(
                        distanceElement, $"{VolumeExtension}.attenuationDistance");
                    if (distance <= 0f)
                    {
                        throw new InvalidDataException(
                            $"glTF '{VolumeExtension}.attenuationDistance' must be positive.");
                    }
                    material.VolumeAttenuationDistance = distance;
                }
                if (volume.TryGetProperty("attenuationColor", out JsonElement colorElement))
                {
                    Vector3 attenuation = ReadUnitVector3(
                        colorElement, $"{VolumeExtension}.attenuationColor");
                    material.VolumeAttenuationColor = new LinearRgba(
                        attenuation.X, attenuation.Y, attenuation.Z, 1f,
                        StandardColorSpaces.LinearSrgb);
                }
            }
            if (sheenExtension is JsonElement sheen)
            {
                Vector3 sheenColor = sheen.TryGetProperty(
                    "sheenColorFactor", out JsonElement colorFactor)
                    ? ReadUnitVector3(colorFactor, $"{SheenExtension}.sheenColorFactor")
                    : Vector3.Zero;
                material.SheenColor = new LinearRgba(
                    sheenColor.X,
                    sheenColor.Y,
                    sheenColor.Z,
                    1f,
                    StandardColorSpaces.LinearSrgb);
                material.SheenRoughness = sheen.TryGetProperty(
                    "sheenRoughnessFactor", out JsonElement roughnessFactor)
                    ? ReadUnitFloat(roughnessFactor, $"{SheenExtension}.sheenRoughnessFactor")
                    : 0f;
                if (sheen.TryGetProperty("sheenColorTexture", out JsonElement sheenColorInfo))
                {
                    TextureReference texture = ReadTextureReference(
                        context.Root, sheenColorInfo, $"{SheenExtension}.sheenColorTexture");
                    ColorTextureSource decoded = images.DecodeColor(texture.ImageIndex);
                    material.SheenColorTexture = decoded.Decoded;
                    material.CompressedSheenColorTexture = decoded.Compressed;
                    material.SheenColorTextureMapping = texture.Mapping;
                }
                if (sheen.TryGetProperty("sheenRoughnessTexture", out JsonElement sheenRoughnessInfo))
                {
                    TextureReference texture = ReadTextureReference(
                        context.Root, sheenRoughnessInfo, $"{SheenExtension}.sheenRoughnessTexture");
                    DataTextureSource decoded = images.DecodeData(texture.ImageIndex);
                    material.SheenRoughnessTexture = decoded.Decoded;
                    material.CompressedSheenRoughnessTexture = decoded.Compressed;
                    material.SheenRoughnessTextureMapping = texture.Mapping;
                }
            }
            if (iridescenceExtension is JsonElement iridescence)
            {
                material.IridescenceFactor = iridescence.TryGetProperty(
                    "iridescenceFactor", out JsonElement factorElement)
                    ? ReadUnitFloat(factorElement, $"{IridescenceExtension}.iridescenceFactor")
                    : 0f;
                if (iridescence.TryGetProperty(
                    "iridescenceIor", out JsonElement iridescenceIorElement))
                {
                    float value = ReadFiniteFloat(
                        iridescenceIorElement, $"{IridescenceExtension}.iridescenceIor");
                    if (value < 1f)
                    {
                        throw new InvalidDataException(
                            $"glTF '{IridescenceExtension}.iridescenceIor' must be at least one.");
                    }
                    material.IridescenceIndexOfRefraction = value;
                }
                material.IridescenceThicknessMinimum = iridescence.TryGetProperty(
                    "iridescenceThicknessMinimum", out JsonElement minimumElement)
                    ? ReadNonNegativeFloat(
                        minimumElement, $"{IridescenceExtension}.iridescenceThicknessMinimum")
                    : 100f;
                material.IridescenceThicknessMaximum = iridescence.TryGetProperty(
                    "iridescenceThicknessMaximum", out JsonElement maximumElement)
                    ? ReadNonNegativeFloat(
                        maximumElement, $"{IridescenceExtension}.iridescenceThicknessMaximum")
                    : 400f;
                if (iridescence.TryGetProperty(
                    "iridescenceTexture", out JsonElement iridescenceInfo))
                {
                    TextureReference texture = ReadTextureReference(
                        context.Root, iridescenceInfo, $"{IridescenceExtension}.iridescenceTexture");
                    DataTextureSource decoded = images.DecodeData(texture.ImageIndex);
                    material.IridescenceTexture = decoded.Decoded;
                    material.CompressedIridescenceTexture = decoded.Compressed;
                    material.IridescenceTextureMapping = texture.Mapping;
                }
                if (iridescence.TryGetProperty(
                    "iridescenceThicknessTexture", out JsonElement thicknessInfo))
                {
                    TextureReference texture = ReadTextureReference(
                        context.Root,
                        thicknessInfo,
                        $"{IridescenceExtension}.iridescenceThicknessTexture");
                    DataTextureSource decoded = images.DecodeData(texture.ImageIndex);
                    material.IridescenceThicknessTexture = decoded.Decoded;
                    material.CompressedIridescenceThicknessTexture = decoded.Compressed;
                    material.IridescenceThicknessTextureMapping = texture.Mapping;
                }
            }
            if (specularExtension is JsonElement specular)
            {
                material.SpecularFactor = specular.TryGetProperty(
                    "specularFactor", out JsonElement specularFactorElement)
                    ? ReadUnitFloat(specularFactorElement, $"{SpecularExtension}.specularFactor")
                    : 1f;
                Vector3 specularColor = specular.TryGetProperty(
                    "specularColorFactor", out JsonElement specularColorElement)
                    ? ReadNonNegativeVector3(
                        specularColorElement, $"{SpecularExtension}.specularColorFactor")
                    : Vector3.One;
                material.SpecularColor = new LinearRgba(
                    specularColor.X,
                    specularColor.Y,
                    specularColor.Z,
                    1f,
                    StandardColorSpaces.LinearSrgb);
                if (specular.TryGetProperty("specularTexture", out JsonElement specularInfo))
                {
                    TextureReference texture = ReadTextureReference(
                        context.Root, specularInfo, $"{SpecularExtension}.specularTexture");
                    DataTextureSource decoded = images.DecodeData(texture.ImageIndex);
                    material.SpecularTexture = decoded.Decoded;
                    material.CompressedSpecularTexture = decoded.Compressed;
                    material.SpecularTextureMapping = texture.Mapping;
                }
                if (specular.TryGetProperty(
                    "specularColorTexture", out JsonElement specularColorInfo))
                {
                    TextureReference texture = ReadTextureReference(
                        context.Root,
                        specularColorInfo,
                        $"{SpecularExtension}.specularColorTexture");
                    ColorTextureSource decoded = images.DecodeColor(texture.ImageIndex);
                    material.SpecularColorTexture = decoded.Decoded;
                    material.CompressedSpecularColorTexture = decoded.Compressed;
                    material.SpecularColorTextureMapping = texture.Mapping;
                }
            }
            if (diffuseTransmissionExtension is JsonElement diffuseTransmission)
            {
                material.DiffuseTransmissionFactor = diffuseTransmission.TryGetProperty(
                    "diffuseTransmissionFactor", out JsonElement factorElement)
                    ? ReadUnitFloat(
                        factorElement,
                        $"{DiffuseTransmissionExtension}.diffuseTransmissionFactor")
                    : 0f;
                Vector3 transmissionColor = diffuseTransmission.TryGetProperty(
                    "diffuseTransmissionColorFactor", out JsonElement colorElement)
                    ? ReadUnitVector3(
                        colorElement,
                        $"{DiffuseTransmissionExtension}.diffuseTransmissionColorFactor")
                    : Vector3.One;
                material.DiffuseTransmissionColor = new LinearRgba(
                    transmissionColor.X,
                    transmissionColor.Y,
                    transmissionColor.Z,
                    1f,
                    StandardColorSpaces.LinearSrgb);
                if (diffuseTransmission.TryGetProperty(
                    "diffuseTransmissionTexture", out JsonElement factorInfo))
                {
                    TextureReference texture = ReadTextureReference(
                        context.Root,
                        factorInfo,
                        $"{DiffuseTransmissionExtension}.diffuseTransmissionTexture");
                    DataTextureSource decoded = images.DecodeData(texture.ImageIndex);
                    material.DiffuseTransmissionTexture = decoded.Decoded;
                    material.CompressedDiffuseTransmissionTexture = decoded.Compressed;
                    material.DiffuseTransmissionTextureMapping = texture.Mapping;
                }
                if (diffuseTransmission.TryGetProperty(
                    "diffuseTransmissionColorTexture", out JsonElement colorInfo))
                {
                    TextureReference texture = ReadTextureReference(
                        context.Root,
                        colorInfo,
                        $"{DiffuseTransmissionExtension}.diffuseTransmissionColorTexture");
                    ColorTextureSource decoded = images.DecodeColor(texture.ImageIndex);
                    material.DiffuseTransmissionColorTexture = decoded.Decoded;
                    material.CompressedDiffuseTransmissionColorTexture = decoded.Compressed;
                    material.DiffuseTransmissionColorTextureMapping = texture.Mapping;
                }
            }
            if (source.TryGetProperty("emissiveTexture", out JsonElement emissiveInfo))
            {
                TextureReference texture = ReadTextureReference(
                    context.Root,
                    emissiveInfo,
                    "emissiveTexture");
                ColorTextureSource decoded = images.DecodeColor(texture.ImageIndex);
                material.EmissiveTexture = decoded.Decoded;
                material.CompressedEmissiveTexture = decoded.Compressed;
                material.EmissiveTextureMapping = texture.Mapping;
            }
            if (pbr is JsonElement texturedPbr &&
                texturedPbr.TryGetProperty("baseColorTexture", out JsonElement textureInfo))
            {
                TextureReference texture = ReadTextureReference(
                    context.Root,
                    textureInfo,
                    "baseColorTexture");
                ColorTextureSource decoded = images.DecodeColor(texture.ImageIndex);
                material.BaseColorTexture = decoded.Decoded;
                material.CompressedBaseColorTexture = decoded.Compressed;
                material.BaseColorTextureMapping = texture.Mapping;
            }
            if (source.TryGetProperty("normalTexture", out JsonElement normalInfo))
            {
                TextureReference texture = ReadTextureReference(
                    context.Root,
                    normalInfo,
                    "normalTexture");
                DataTextureSource decoded = images.DecodeData(texture.ImageIndex);
                material.NormalTexture = decoded.Decoded;
                material.CompressedNormalTexture = decoded.Compressed;
                material.NormalScale = normalInfo.TryGetProperty("scale", out JsonElement scale)
                    ? ReadNonNegativeFloat(scale, "normalTexture.scale")
                    : 1f;
                material.NormalTextureMapping = texture.Mapping;
            }
            TextureReference? metallicRoughnessTexture = null;
            if (pbr is JsonElement materialPbr &&
                materialPbr.TryGetProperty("metallicRoughnessTexture", out JsonElement ormInfo))
            {
                metallicRoughnessTexture = ReadTextureReference(
                    context.Root,
                    ormInfo,
                    "metallicRoughnessTexture");
            }
            TextureReference? occlusionTexture = null;
            float occlusionStrength = 1f;
            if (source.TryGetProperty("occlusionTexture", out JsonElement occlusionInfo))
            {
                occlusionTexture = ReadTextureReference(
                    context.Root,
                    occlusionInfo,
                    "occlusionTexture");
                occlusionStrength = occlusionInfo.TryGetProperty("strength", out JsonElement strength)
                    ? ReadUnitFloat(strength, "occlusionTexture.strength")
                    : 1f;
            }
            if (metallicRoughnessTexture is not null || occlusionTexture is not null)
            {
                if (metallicRoughnessTexture is TextureReference mappedMr &&
                    occlusionTexture is TextureReference mappedAo &&
                    mappedMr.Mapping != mappedAo.Mapping)
                {
                    throw new NotSupportedException(
                        "Packing separate glTF occlusion and metallic/roughness inputs requires matching sampler and UV mappings.");
                }
                if (metallicRoughnessTexture is TextureReference mr &&
                    occlusionTexture is TextureReference ao &&
                    mr.ImageIndex == ao.ImageIndex &&
                    occlusionStrength == 1f)
                {
                    DataTextureSource decoded = images.DecodeData(mr.ImageIndex);
                    material.OcclusionRoughnessMetallicTexture = decoded.Decoded;
                    material.CompressedOcclusionRoughnessMetallicTexture = decoded.Compressed;
                }
                else
                {
                    material.OcclusionRoughnessMetallicTexture = CombineOrm(
                        metallicRoughnessTexture is TextureReference decodedMr
                            ? images.DecodeDataFallback(decodedMr.ImageIndex)
                            : null,
                        occlusionTexture is TextureReference decodedAo
                            ? images.DecodeDataFallback(decodedAo.ImageIndex)
                            : null,
                        occlusionStrength,
                        material.Name);
                }
                material.OcclusionRoughnessMetallicTextureMapping =
                    (metallicRoughnessTexture ?? occlusionTexture)!.Value.Mapping;
            }
            string alphaMode = ReadOptionalString(source, "alphaMode") ?? "OPAQUE";
            material.AlphaMode = alphaMode switch
            {
                "OPAQUE" => MaterialAlphaMode.Opaque,
                "MASK" => MaterialAlphaMode.Mask,
                "BLEND" => MaterialAlphaMode.Blend,
                _ => throw new InvalidDataException($"Unknown glTF alphaMode '{alphaMode}'."),
            };
            if (source.TryGetProperty("alphaCutoff", out JsonElement cutoff))
            {
                material.AlphaCutoff = ReadUnitFloat(cutoff, "alphaCutoff");
            }
            materials[index] = material;
        }
        return materials;
    }

    private static TextureReference ReadTextureReference(
        JsonElement root,
        JsonElement textureInfo,
        string semantic)
    {
        int textureCoordinates = textureInfo.TryGetProperty("texCoord", out JsonElement texCoord)
            ? ReadNonNegativeInt(texCoord, $"{semantic}.texCoord")
            : 0;
        Vector2 scale = Vector2.One;
        Vector2 offset = Vector2.Zero;
        float rotation = 0f;
        if (textureInfo.TryGetProperty("extensions", out JsonElement textureInfoExtensions))
        {
            if (textureInfoExtensions.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("glTF texture-info extensions must be an object.");
            }
            foreach (JsonProperty extension in textureInfoExtensions.EnumerateObject())
            {
                if (extension.Name != TextureTransformExtension)
                {
                    throw new NotSupportedException(
                        $"glTF texture-info extension '{extension.Name}' is not supported yet.");
                }
                if (extension.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException(
                        $"glTF {TextureTransformExtension} must be an object.");
                }
                JsonElement transform = extension.Value;
                if (transform.TryGetProperty("scale", out JsonElement transformScale))
                {
                    float[] values = ReadFloatArray(
                        transformScale,
                        2,
                        $"{semantic}.extensions.{TextureTransformExtension}.scale");
                    scale = new Vector2(values[0], values[1]);
                }
                if (transform.TryGetProperty("offset", out JsonElement transformOffset))
                {
                    float[] values = ReadFloatArray(
                        transformOffset,
                        2,
                        $"{semantic}.extensions.{TextureTransformExtension}.offset");
                    offset = new Vector2(values[0], values[1]);
                }
                if (transform.TryGetProperty("rotation", out JsonElement transformRotation))
                {
                    rotation = transformRotation.GetSingle();
                    if (!float.IsFinite(rotation))
                    {
                        throw new InvalidDataException(
                            $"glTF '{semantic}.extensions.{TextureTransformExtension}.rotation' must be finite.");
                    }
                }
                if (transform.TryGetProperty("texCoord", out JsonElement transformTexCoord))
                {
                    textureCoordinates = ReadNonNegativeInt(
                        transformTexCoord,
                        $"{semantic}.extensions.{TextureTransformExtension}.texCoord");
                }
            }
        }
        if (textureCoordinates is < 0 or > 1)
        {
            throw new NotSupportedException("The current glTF slice supports TEXCOORD_0 and TEXCOORD_1 only.");
        }
        JsonElement textures = RequireArray(root, "textures");
        JsonElement texture = At(
            textures.EnumerateArray().ToArray(),
            ReadIndex(RequireProperty(textureInfo, "index"), $"{semantic}.index"),
            "texture");
        int imageIndex = texture.TryGetProperty("extensions", out JsonElement textureExtensions)
            ? ReadExtendedTextureSource(root, textureExtensions)
            : ReadIndex(RequireProperty(texture, "source"), "texture.source");
        MaterialTextureSampling sampling = MaterialTextureSampling.RepeatingLinear;
        if (texture.TryGetProperty("sampler", out JsonElement samplerElement))
        {
            JsonElement sampler = At(
                RequireArray(root, "samplers").EnumerateArray().ToArray(),
                ReadIndex(samplerElement, "texture.sampler"),
                "sampler");
            (MaterialTextureFilter magnification, MaterialTextureFilter minification,
                MaterialTextureFilter mip, bool useMipmaps) = ReadTextureFilters(sampler);
            sampling = new MaterialTextureSampling(
                ReadAddressMode(sampler, "wrapS"),
                ReadAddressMode(sampler, "wrapT"),
                magnification,
                minification,
                mip,
                useMipmaps);
        }
        return new TextureReference(
            imageIndex,
            new MaterialTextureMapping(
                sampling,
                textureCoordinates,
                scale,
                offset,
                rotation));
    }

    private static int ReadExtendedTextureSource(JsonElement root, JsonElement extensions)
    {
        if (extensions.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("glTF texture extensions must be an object.");
        }
        JsonElement? basis = null;
        foreach (JsonProperty extension in extensions.EnumerateObject())
        {
            if (extension.Name != BasisTextureExtension)
            {
                throw new NotSupportedException(
                    $"glTF texture extension '{extension.Name}' is not supported yet.");
            }
            basis = extension.Value;
        }
        if (basis is not JsonElement basisSource || basisSource.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException(
                $"A glTF {BasisTextureExtension} texture requires an extension object.");
        }
        int imageIndex = ReadIndex(
            RequireProperty(basisSource, "source"),
            $"texture.extensions.{BasisTextureExtension}.source");
        JsonElement image = At(
            RequireArray(root, "images").EnumerateArray().ToArray(),
            imageIndex,
            $"{BasisTextureExtension} image");
        string? mimeType = ReadOptionalString(image, "mimeType");
        if (mimeType is null && image.TryGetProperty("uri", out JsonElement uriElement))
        {
            string uri = uriElement.GetString() ??
                throw new InvalidDataException("Image URI must be a string.");
            if (uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                mimeType = ReadDataUriMimeType(uri);
            }
            else
            {
                mimeType = InferImageMimeType(uri);
            }
        }
        if (NormalizeImageMimeType(mimeType) != "image/ktx2")
        {
            throw new InvalidDataException(
                $"A glTF {BasisTextureExtension} source must reference an image/ktx2 image.");
        }
        return imageIndex;
    }

    private static NormalizedRgbaDataImage CombineOrm(
        NormalizedRgbaDataImage? metallicRoughness,
        NormalizedRgbaDataImage? occlusion,
        float occlusionStrength,
        string? materialName)
    {
        NormalizedRgbaDataImage dimensions = metallicRoughness ?? occlusion ??
            throw new InvalidOperationException("At least one packed material map is required.");
        if (metallicRoughness is not null && occlusion is not null &&
            (metallicRoughness.Width != occlusion.Width || metallicRoughness.Height != occlusion.Height))
        {
            throw new NotSupportedException(
                "The current packed ORM path requires glTF occlusion and metallic/roughness maps to have matching dimensions.");
        }
        Vector4[] texels = new Vector4[checked((int)(dimensions.Width * dimensions.Height))];
        for (int index = 0; index < texels.Length; index++)
        {
            Vector4 mr = metallicRoughness?.Texels[index] ?? Vector4.One;
            float sampledOcclusion = occlusion?.Texels[index].X ?? 1f;
            float weightedOcclusion = 1f + occlusionStrength * (sampledOcclusion - 1f);
            texels[index] = new Vector4(weightedOcclusion, mr.Y, mr.Z, 1f);
        }
        return NormalizedRgbaDataImage.FromOwnedTexels(
            dimensions.Width,
            dimensions.Height,
            texels,
            $"{materialName ?? "material"} packed ORM");
    }

    private static MaterialTextureAddressMode ReadAddressMode(JsonElement sampler, string property)
    {
        int value = sampler.TryGetProperty(property, out JsonElement element)
            ? ReadNonNegativeInt(element, $"sampler.{property}")
            : 10497;
        return value switch
        {
            33071 => MaterialTextureAddressMode.ClampToEdge,
            33648 => MaterialTextureAddressMode.MirrorRepeat,
            10497 => MaterialTextureAddressMode.Repeat,
            _ => throw new InvalidDataException($"Unknown glTF sampler wrap value {value}."),
        };
    }

    private static (
        MaterialTextureFilter Magnification,
        MaterialTextureFilter Minification,
        MaterialTextureFilter Mip,
        bool UseMipmaps) ReadTextureFilters(JsonElement sampler)
    {
        int mag = sampler.TryGetProperty("magFilter", out JsonElement magElement)
            ? ReadNonNegativeInt(magElement, "sampler.magFilter")
            : 9729;
        int min = sampler.TryGetProperty("minFilter", out JsonElement minElement)
            ? ReadNonNegativeInt(minElement, "sampler.minFilter")
            : 9987;
        if (mag is not (9728 or 9729) || min is not (9728 or 9729 or 9984 or 9985 or 9986 or 9987))
        {
            throw new InvalidDataException("Unknown glTF sampler filter value.");
        }
        MaterialTextureFilter magnification = mag == 9728
            ? MaterialTextureFilter.Nearest
            : MaterialTextureFilter.Linear;
        MaterialTextureFilter minification = min is 9728 or 9984 or 9986
            ? MaterialTextureFilter.Nearest
            : MaterialTextureFilter.Linear;
        MaterialTextureFilter mip = min is 9986 or 9987
            ? MaterialTextureFilter.Linear
            : MaterialTextureFilter.Nearest;
        return (magnification, minification, mip, min is not (9728 or 9729));
    }

    private static GltfMaterialVariant[] ReadMaterialVariants(JsonElement root)
    {
        if (!root.TryGetProperty("extensions", out JsonElement extensions) ||
            !extensions.TryGetProperty(MaterialVariantsExtension, out JsonElement variantsExtension))
        {
            return [];
        }
        if (variantsExtension.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"glTF {MaterialVariantsExtension} must be an object.");
        }
        JsonElement variants = RequireArray(variantsExtension, "variants");
        GltfMaterialVariant[] result = new GltfMaterialVariant[variants.GetArrayLength()];
        for (int index = 0; index < result.Length; index++)
        {
            string name = ReadOptionalString(variants[index], "name") ??
                throw new InvalidDataException(
                    $"glTF {MaterialVariantsExtension}.variants[{index}] requires a name.");
            result[index] = new GltfMaterialVariant(index, name);
        }
        return result;
    }

    private static MeshDefinition[] ReadMeshes(ImportContext context, int materialVariantCount)
    {
        JsonElement meshes = RequireArray(context.Root, "meshes");
        MeshDefinition[] result = new MeshDefinition[meshes.GetArrayLength()];
        for (int meshIndex = 0; meshIndex < result.Length; meshIndex++)
        {
            JsonElement primitives = RequireArray(meshes[meshIndex], "primitives");
            MeshGeometry[] primitiveGeometries = new MeshGeometry[primitives.GetArrayLength()];
            IReadOnlyDictionary<int, int>[] primitiveMaterialVariants =
                new IReadOnlyDictionary<int, int>[primitives.GetArrayLength()];
            int? morphTargetCount = null;
            for (int primitiveIndex = 0; primitiveIndex < primitiveGeometries.Length; primitiveIndex++)
            {
                JsonElement primitive = primitives[primitiveIndex];
                primitiveMaterialVariants[primitiveIndex] = ReadPrimitiveMaterialVariants(
                    primitive,
                    materialVariantCount);
                int mode = primitive.TryGetProperty("mode", out JsonElement modeElement)
                    ? ReadNonNegativeInt(modeElement, "primitive.mode")
                    : 4;
                if (mode != 4)
                {
                    throw new NotSupportedException("The initial glTF importer supports triangle primitives only.");
                }
                JsonElement attributes = RequireObject(primitive, "attributes");
                if (attributes.TryGetProperty("COLOR_0", out _))
                {
                    throw new NotSupportedException(
                        "glTF vertex colors are not supported by the current progressive slice.");
                }
                int positionAccessor = ReadIndex(RequireProperty(attributes, "POSITION"), "POSITION accessor");
                Vector3[] positions = context.ReadVector3Accessor(positionAccessor, "POSITION");
                uint[] indices = primitive.TryGetProperty("indices", out JsonElement indicesElement)
                    ? context.ReadIndexAccessor(ReadIndex(indicesElement, "indices accessor"))
                    : Enumerable.Range(0, positions.Length).Select(static value => checked((uint)value)).ToArray();
                if (indices.Length % 3 != 0)
                {
                    throw new InvalidDataException("A triangle primitive index count must be divisible by three.");
                }
                Vector3[] normals = attributes.TryGetProperty("NORMAL", out JsonElement normalElement)
                    ? context.ReadVector3Accessor(ReadIndex(normalElement, "NORMAL accessor"), "NORMAL")
                    : GenerateNormals(positions, indices);
                Vector2[] uvs = attributes.TryGetProperty("TEXCOORD_0", out JsonElement uvElement)
                    ? context.ReadVector2Accessor(ReadIndex(uvElement, "TEXCOORD_0 accessor"), "TEXCOORD_0")
                    : [];
                Vector2[] uvs1 = attributes.TryGetProperty("TEXCOORD_1", out JsonElement uv1Element)
                    ? context.ReadVector2Accessor(ReadIndex(uv1Element, "TEXCOORD_1 accessor"), "TEXCOORD_1")
                    : [];
                Vector4[] tangents = attributes.TryGetProperty("TANGENT", out JsonElement tangentElement)
                    ? context.ReadVector4Accessor(ReadIndex(tangentElement, "TANGENT accessor"), "TANGENT")
                    : [];
                bool hasJointIndices = attributes.TryGetProperty("JOINTS_0", out JsonElement jointElement);
                bool hasJointWeights = attributes.TryGetProperty("WEIGHTS_0", out JsonElement jointWeightElement);
                if (hasJointIndices != hasJointWeights)
                {
                    throw new InvalidDataException("glTF JOINTS_0 and WEIGHTS_0 must be supplied together.");
                }
                JointIndices4[] jointIndices = hasJointIndices
                    ? context.ReadJointIndicesAccessor(ReadIndex(jointElement, "JOINTS_0 accessor"))
                    : [];
                Vector4[] jointWeights = hasJointWeights
                    ? context.ReadVector4Accessor(ReadIndex(jointWeightElement, "WEIGHTS_0 accessor"), "WEIGHTS_0")
                    : [];
                MorphTarget[] morphTargets = ReadMorphTargets(context, primitive, positions.Length, normals.Length);
                morphTargetCount ??= morphTargets.Length;
                if (morphTargetCount.Value != morphTargets.Length)
                {
                    throw new NotSupportedException(
                        "All primitives in an animated glTF mesh must use the same morph-target count.");
                }
                primitiveGeometries[primitiveIndex] = MeshGeometry.CreateWithTextureCoordinateSets(
                    positions,
                    indices,
                    uvs1,
                    normals,
                    uvs,
                    jointIndices: jointIndices,
                    jointWeights: jointWeights,
                    morphTargets: morphTargets,
                    tangents: tangents);
            }
            int targetCount = morphTargetCount ?? 0;
            float[] weights = meshes[meshIndex].TryGetProperty("weights", out JsonElement weightElement)
                ? ReadFiniteFloatArray(weightElement, targetCount, "mesh.weights")
                : new float[targetCount];
            result[meshIndex] = new MeshDefinition(
                primitiveGeometries,
                weights,
                primitiveMaterialVariants);
        }
        return result;
    }

    private static IReadOnlyDictionary<int, int> ReadPrimitiveMaterialVariants(
        JsonElement primitive,
        int materialVariantCount)
    {
        if (!primitive.TryGetProperty("extensions", out JsonElement extensions) ||
            !extensions.TryGetProperty(MaterialVariantsExtension, out JsonElement variantsExtension))
        {
            return new Dictionary<int, int>();
        }
        if (materialVariantCount == 0)
        {
            throw new InvalidDataException(
                $"A primitive uses {MaterialVariantsExtension} without root variant definitions.");
        }
        JsonElement mappings = RequireArray(variantsExtension, "mappings");
        Dictionary<int, int> result = [];
        foreach (JsonElement mapping in mappings.EnumerateArray())
        {
            int materialIndex = ReadIndex(RequireProperty(mapping, "material"), "variant material");
            JsonElement variants = RequireArray(mapping, "variants");
            foreach (JsonElement variantElement in variants.EnumerateArray())
            {
                int variantIndex = ReadIndex(variantElement, "material variant");
                if ((uint)variantIndex >= (uint)materialVariantCount)
                {
                    throw new InvalidDataException("A primitive material-variant index is out of range.");
                }
                if (!result.TryAdd(variantIndex, materialIndex))
                {
                    throw new InvalidDataException(
                        "A material variant may appear at most once per mesh primitive.");
                }
            }
        }
        return result;
    }

    private static NodeImport ReadNodes(
        JsonElement root,
        MeshDefinition[] meshDefinitions,
        Material[] materials,
        PunctualLightDefinition[] punctualLights)
    {
        JsonElement nodesElement = RequireArray(root, "nodes");
        SceneNode[] nodes = new SceneNode[nodesElement.GetArrayLength()];
        Mesh[][] nodeMeshes = new Mesh[nodes.Length][];
        List<GltfMaterialVariantBinding> materialVariantBindings = [];
        for (int nodeIndex = 0; nodeIndex < nodes.Length; nodeIndex++)
        {
            JsonElement source = nodesElement[nodeIndex];
            string? nodeName = ReadOptionalString(source, "name");
            SceneNode node = new(nodeName);
            ReadTransform(source, node.Transform);
            nodes[nodeIndex] = node;
            nodeMeshes[nodeIndex] = [];
            if (source.TryGetProperty("extensions", out JsonElement extensions) &&
                extensions.TryGetProperty(PunctualLightsExtension, out JsonElement lightExtension))
            {
                int lightIndex = ReadIndex(
                    RequireProperty(lightExtension, "light"),
                    $"node.extensions.{PunctualLightsExtension}.light");
                node.AddChild(At(punctualLights, lightIndex, "punctual light").CreateInstance());
            }
            if (!source.TryGetProperty("mesh", out JsonElement meshElement))
            {
                continue;
            }
            int meshIndex = ReadIndex(meshElement, "node.mesh");
            MeshDefinition definition = At(meshDefinitions, meshIndex, "mesh");
            MeshGeometry[] primitives = definition.Primitives;
            float[] weights = source.TryGetProperty("weights", out JsonElement nodeWeights)
                ? ReadFiniteFloatArray(nodeWeights, definition.Weights.Length, "node.weights")
                : definition.Weights;
            JsonElement primitiveElements = RequireArray(RequireArray(root, "meshes")[meshIndex], "primitives");
            Mesh[] createdMeshes = new Mesh[primitives.Length];
            for (int primitiveIndex = 0; primitiveIndex < primitives.Length; primitiveIndex++)
            {
                JsonElement primitive = primitiveElements[primitiveIndex];
                Material material = primitive.TryGetProperty("material", out JsonElement materialElement)
                    ? At(materials, ReadIndex(materialElement, "primitive.material"), "material")
                    : CreateDefaultMaterial();
                Mesh created = new(
                    primitives[primitiveIndex],
                    material,
                    primitives.Length == 1 ? nodeName : $"{nodeName ?? "mesh"} primitive {primitiveIndex}");
                created.MorphWeights = weights;
                node.AddChild(created);
                createdMeshes[primitiveIndex] = created;
                IReadOnlyDictionary<int, int> variantMaterialIndices =
                    definition.MaterialVariants[primitiveIndex];
                if (variantMaterialIndices.Count > 0)
                {
                    Dictionary<int, Material> variantMaterials = [];
                    foreach ((int variantIndex, int materialIndex) in variantMaterialIndices)
                    {
                        variantMaterials.Add(
                            variantIndex,
                            At(materials, materialIndex, "variant material"));
                    }
                    materialVariantBindings.Add(new GltfMaterialVariantBinding(
                        created,
                        material,
                        variantMaterials));
                }
            }
            nodeMeshes[nodeIndex] = createdMeshes;
        }
        return new NodeImport(nodes, nodeMeshes, materialVariantBindings.ToArray());
    }

    private static PunctualLightDefinition[] ReadPunctualLights(JsonElement root)
    {
        if (!root.TryGetProperty("extensions", out JsonElement extensions) ||
            !extensions.TryGetProperty(PunctualLightsExtension, out JsonElement lightExtension))
        {
            return [];
        }
        JsonElement lights = RequireArray(lightExtension, "lights");
        PunctualLightDefinition[] result = new PunctualLightDefinition[lights.GetArrayLength()];
        for (int index = 0; index < result.Length; index++)
        {
            JsonElement source = lights[index];
            string type = RequireProperty(source, "type").GetString() ??
                throw new InvalidDataException($"glTF '{PunctualLightsExtension}.lights[{index}].type' must be a string.");
            Vector3 color = source.TryGetProperty("color", out JsonElement colorElement)
                ? ReadUnitVector3(colorElement, $"{PunctualLightsExtension}.lights[{index}].color")
                : Vector3.One;
            float intensity = source.TryGetProperty("intensity", out JsonElement intensityElement)
                ? ReadNonNegativeFloat(
                    intensityElement,
                    $"{PunctualLightsExtension}.lights[{index}].intensity")
                : 1f;
            float range = source.TryGetProperty("range", out JsonElement rangeElement)
                ? ReadFiniteFloat(rangeElement, $"{PunctualLightsExtension}.lights[{index}].range")
                : float.PositiveInfinity;
            if (range <= 0f)
            {
                throw new InvalidDataException(
                    $"glTF '{PunctualLightsExtension}.lights[{index}].range' must be positive.");
            }
            if (type == "directional" && source.TryGetProperty("range", out _))
            {
                throw new InvalidDataException(
                    $"glTF '{PunctualLightsExtension}.lights[{index}].range' is not valid for a directional light.");
            }

            float innerConeAngle = 0f;
            float outerConeAngle = MathF.PI / 4f;
            if (type == "spot")
            {
                JsonElement spot = RequireObject(source, "spot");
                innerConeAngle = spot.TryGetProperty("innerConeAngle", out JsonElement innerElement)
                    ? ReadFiniteFloat(
                        innerElement,
                        $"{PunctualLightsExtension}.lights[{index}].spot.innerConeAngle")
                    : 0f;
                outerConeAngle = spot.TryGetProperty("outerConeAngle", out JsonElement outerElement)
                    ? ReadFiniteFloat(
                        outerElement,
                        $"{PunctualLightsExtension}.lights[{index}].spot.outerConeAngle")
                    : MathF.PI / 4f;
                if (innerConeAngle < 0f || innerConeAngle >= outerConeAngle ||
                    outerConeAngle > MathF.PI / 2f)
                {
                    throw new InvalidDataException(
                        $"glTF spot-light angles must satisfy 0 <= inner < outer <= pi/2.");
                }
            }
            else if (type is not ("point" or "directional"))
            {
                throw new InvalidDataException(
                    $"Unknown glTF punctual-light type '{type}'.");
            }

            result[index] = new PunctualLightDefinition(
                type,
                new LinearRgba(color.X, color.Y, color.Z, 1f, StandardColorSpaces.LinearSrgb),
                intensity,
                range,
                innerConeAngle,
                outerConeAngle,
                ReadOptionalString(source, "name"));
        }
        return result;
    }

    private static void AttachSkins(ImportContext context, NodeImport nodes)
    {
        if (!context.Root.TryGetProperty("skins", out JsonElement skinArray))
        {
            return;
        }
        JsonElement skins = AsArray(skinArray, "skins");
        Skin?[] importedSkins = new Skin?[skins.GetArrayLength()];
        JsonElement sourceNodes = RequireArray(context.Root, "nodes");
        for (int nodeIndex = 0; nodeIndex < sourceNodes.GetArrayLength(); nodeIndex++)
        {
            if (!sourceNodes[nodeIndex].TryGetProperty("skin", out JsonElement skinElement))
            {
                continue;
            }
            Mesh[] meshes = nodes.Meshes[nodeIndex];
            if (meshes.Length == 0)
            {
                throw new InvalidDataException("A glTF skin is attached to a node without a mesh.");
            }
            int skinIndex = ReadIndex(skinElement, "node.skin");
            if ((uint)skinIndex >= (uint)importedSkins.Length)
            {
                throw new InvalidDataException("A glTF skin index is out of range.");
            }
            Skin skin = importedSkins[skinIndex] ??= ReadSkin(context, skins[skinIndex], nodes.Nodes);
            foreach (Mesh mesh in meshes)
            {
                if (mesh.Geometry.JointIndices.Count == 0 ||
                    mesh.Geometry.JointIndices.Any(index => index.Maximum >= skin.Joints.Count))
                {
                    throw new InvalidDataException("Skinned glTF geometry references an unavailable joint.");
                }
                mesh.Skin = skin;
            }
        }
    }

    private static Skin ReadSkin(ImportContext context, JsonElement source, SceneNode[] nodes)
    {
        JsonElement jointElements = RequireArray(source, "joints");
        SceneNode[] joints = jointElements.EnumerateArray()
            .Select(element => At(nodes, ReadIndex(element, "skin joint"), "skin joint"))
            .ToArray();
        Matrix4x4[] inverseBindMatrices = source.TryGetProperty(
            "inverseBindMatrices",
            out JsonElement inverseBindAccessor)
                ? context.ReadMatrix4x4Accessor(
                    ReadIndex(inverseBindAccessor, "skin.inverseBindMatrices"),
                    "inverse bind matrices")
                : Enumerable.Repeat(Matrix4x4.Identity, joints.Length).ToArray();
        if (inverseBindMatrices.Length != joints.Length)
        {
            throw new InvalidDataException("A glTF skin requires one inverse bind matrix per joint.");
        }
        if (source.TryGetProperty("skeleton", out JsonElement skeletonElement))
        {
            _ = At(nodes, ReadIndex(skeletonElement, "skin.skeleton"), "skin skeleton");
        }
        return new Skin(joints, inverseBindMatrices, ReadOptionalString(source, "name"));
    }

    private static MorphTarget[] ReadMorphTargets(
        ImportContext context,
        JsonElement primitive,
        int positionCount,
        int normalCount)
    {
        if (!primitive.TryGetProperty("targets", out JsonElement targetElement))
        {
            return [];
        }
        JsonElement targets = AsArray(targetElement, "primitive.targets");
        MorphTarget[] result = new MorphTarget[targets.GetArrayLength()];
        for (int index = 0; index < result.Length; index++)
        {
            JsonElement target = targets[index];
            Vector3[] positions = target.TryGetProperty("POSITION", out JsonElement positionAccessor)
                ? context.ReadVector3Accessor(ReadIndex(positionAccessor, "morph POSITION accessor"), "morph POSITION")
                : new Vector3[positionCount];
            Vector3[] normals = target.TryGetProperty("NORMAL", out JsonElement normalAccessor)
                ? context.ReadVector3Accessor(ReadIndex(normalAccessor, "morph NORMAL accessor"), "morph NORMAL")
                : [];
            Vector3[] tangents = target.TryGetProperty("TANGENT", out JsonElement tangentAccessor)
                ? context.ReadVector3Accessor(ReadIndex(tangentAccessor, "morph TANGENT accessor"), "morph TANGENT")
                : [];
            if (positions.Length != positionCount ||
                (normals.Length != 0 && normals.Length != normalCount) ||
                (tangents.Length != 0 && tangents.Length != positionCount))
            {
                throw new InvalidDataException("glTF morph accessors must match the base vertex count.");
            }
            result[index] = new MorphTarget(
                positions,
                normals,
                $"glTF morph {index}",
                tangents);
        }
        return result;
    }

    private static AnimationClip[] ReadAnimations(ImportContext context, NodeImport nodes)
    {
        if (!context.Root.TryGetProperty("animations", out JsonElement animationElement))
        {
            return [];
        }
        JsonElement animations = AsArray(animationElement, "animations");
        AnimationClip[] result = new AnimationClip[animations.GetArrayLength()];
        for (int animationIndex = 0; animationIndex < result.Length; animationIndex++)
        {
            JsonElement animation = animations[animationIndex];
            JsonElement samplers = RequireArray(animation, "samplers");
            JsonElement channels = RequireArray(animation, "channels");
            List<AnimationTrack> tracks = [];
            foreach (JsonElement channel in channels.EnumerateArray())
            {
                JsonElement target = RequireObject(channel, "target");
                string path = ReadOptionalString(target, "path") ??
                    throw new InvalidDataException("An animation channel requires a target path.");
                int nodeIndex = ReadIndex(RequireProperty(target, "node"), "animation target node");
                SceneNode targetNode = At(nodes.Nodes, nodeIndex, "animation target node");
                int samplerIndex = ReadIndex(RequireProperty(channel, "sampler"), "animation sampler");
                JsonElement sampler = samplerIndex < samplers.GetArrayLength()
                    ? samplers[samplerIndex]
                    : throw new InvalidDataException("An animation sampler index is out of range.");
                AnimationInterpolation interpolation = (ReadOptionalString(sampler, "interpolation") ?? "LINEAR") switch
                {
                    "LINEAR" => AnimationInterpolation.Linear,
                    "STEP" => AnimationInterpolation.Step,
                    "CUBICSPLINE" => AnimationInterpolation.CubicSpline,
                    _ => throw new NotSupportedException("The glTF animation interpolation is not supported."),
                };
                float[] times = context.ReadFloatAccessor(
                    ReadIndex(RequireProperty(sampler, "input"), "animation input accessor"),
                    "animation input");
                int outputAccessor = ReadIndex(
                    RequireProperty(sampler, "output"),
                    "animation output accessor");
                switch (path)
                {
                    case "translation":
                    case "scale":
                        Vector3[] vectors = context.ReadVector3Accessor(outputAccessor, $"animation {path}");
                        int expectedVectorCount = checked(
                            times.Length * (interpolation == AnimationInterpolation.CubicSpline ? 3 : 1));
                        if (vectors.Length != expectedVectorCount)
                        {
                            throw new InvalidDataException("Transform animation output count must match its keys.");
                        }
                        Vector3AnimationTarget vectorTarget = path == "translation"
                            ? Vector3AnimationTarget.Position
                            : Vector3AnimationTarget.Scale;
                        tracks.Add(interpolation == AnimationInterpolation.CubicSpline
                            ? CreateCubicVectorTrack(targetNode, vectorTarget, times, vectors)
                            : new Vector3AnimationTrack(
                                targetNode,
                                vectorTarget,
                                times,
                                vectors,
                                interpolation));
                        break;
                    case "rotation":
                        Vector4[] vectors4 = context.ReadVector4Accessor(outputAccessor, "animation rotation");
                        int expectedRotationCount = checked(
                            times.Length * (interpolation == AnimationInterpolation.CubicSpline ? 3 : 1));
                        if (vectors4.Length != expectedRotationCount)
                        {
                            throw new InvalidDataException("Rotation animation output count must match its keys.");
                        }
                        tracks.Add(interpolation == AnimationInterpolation.CubicSpline
                            ? CreateCubicQuaternionTrack(targetNode, times, vectors4)
                            : new QuaternionAnimationTrack(
                                targetNode,
                                times,
                                vectors4.Select(static value =>
                                    new Quaternion(value.X, value.Y, value.Z, value.W)),
                                interpolation));
                        break;
                    case "weights":
                        AddMorphTracks(
                            context,
                            nodes,
                            nodeIndex,
                            outputAccessor,
                            times,
                            interpolation,
                            tracks);
                        break;
                    default:
                        throw new NotSupportedException(
                            $"The current glTF animation slice does not support '{path}'.");
                }
            }
            if (tracks.Count == 0)
            {
                throw new InvalidDataException("A glTF animation must contain supported channels.");
            }
            result[animationIndex] = new AnimationClip(tracks, ReadOptionalString(animation, "name"));
        }
        return result;
    }

    private static Vector3AnimationTrack CreateCubicVectorTrack(
        SceneNode target,
        Vector3AnimationTarget property,
        float[] times,
        Vector3[] triplets)
    {
        Vector3[] values = new Vector3[times.Length];
        Vector3[] inTangents = new Vector3[times.Length];
        Vector3[] outTangents = new Vector3[times.Length];
        for (int key = 0; key < times.Length; key++)
        {
            inTangents[key] = triplets[(key * 3) + 0];
            values[key] = triplets[(key * 3) + 1];
            outTangents[key] = triplets[(key * 3) + 2];
        }
        return new Vector3AnimationTrack(target, property, times, values, inTangents, outTangents);
    }

    private static QuaternionAnimationTrack CreateCubicQuaternionTrack(
        SceneNode target,
        float[] times,
        Vector4[] triplets)
    {
        Quaternion[] values = new Quaternion[times.Length];
        Vector4[] inTangents = new Vector4[times.Length];
        Vector4[] outTangents = new Vector4[times.Length];
        for (int key = 0; key < times.Length; key++)
        {
            inTangents[key] = triplets[(key * 3) + 0];
            Vector4 value = triplets[(key * 3) + 1];
            values[key] = new Quaternion(value.X, value.Y, value.Z, value.W);
            outTangents[key] = triplets[(key * 3) + 2];
        }
        return new QuaternionAnimationTrack(target, times, values, inTangents, outTangents);
    }

    private static void AddMorphTracks(
        ImportContext context,
        NodeImport nodes,
        int nodeIndex,
        int outputAccessor,
        float[] times,
        AnimationInterpolation interpolation,
        List<AnimationTrack> tracks)
    {
        Mesh[] targetMeshes = At(nodes.Meshes, nodeIndex, "animation target node");
        if (targetMeshes.Length == 0 || targetMeshes[0].Geometry.MorphTargets.Count == 0)
        {
            throw new InvalidDataException("A morph animation targets a node without morph geometry.");
        }
        float[] flattened = context.ReadFloatAccessor(outputAccessor, "animation weights");
        int targetCount = targetMeshes[0].Geometry.MorphTargets.Count;
        int valuesPerKey = interpolation == AnimationInterpolation.CubicSpline ? 3 : 1;
        if (flattened.Length != checked(times.Length * targetCount * valuesPerKey))
        {
            throw new InvalidDataException("Morph animation output count does not match keys and targets.");
        }
        IReadOnlyList<float>[] keys = new IReadOnlyList<float>[times.Length];
        IReadOnlyList<float>[]? inTangents = interpolation == AnimationInterpolation.CubicSpline
            ? new IReadOnlyList<float>[times.Length]
            : null;
        IReadOnlyList<float>[]? outTangents = interpolation == AnimationInterpolation.CubicSpline
            ? new IReadOnlyList<float>[times.Length]
            : null;
        for (int key = 0; key < keys.Length; key++)
        {
            float[] weights = new float[targetCount];
            int keyOffset = key * targetCount * valuesPerKey;
            if (interpolation == AnimationInterpolation.CubicSpline)
            {
                float[] incoming = new float[targetCount];
                float[] outgoing = new float[targetCount];
                Array.Copy(flattened, keyOffset, incoming, 0, targetCount);
                Array.Copy(flattened, keyOffset + targetCount, weights, 0, targetCount);
                Array.Copy(flattened, keyOffset + (targetCount * 2), outgoing, 0, targetCount);
                inTangents![key] = incoming;
                outTangents![key] = outgoing;
            }
            else
            {
                Array.Copy(flattened, keyOffset, weights, 0, targetCount);
            }
            keys[key] = weights;
        }
        foreach (Mesh mesh in targetMeshes)
        {
            if (mesh.Geometry.MorphTargets.Count != targetCount)
            {
                throw new NotSupportedException("Morph-animated primitives require matching target counts.");
            }
            tracks.Add(interpolation == AnimationInterpolation.CubicSpline
                ? new MorphWeightAnimationTrack(mesh, times, keys, inTangents!, outTangents!)
                : new MorphWeightAnimationTrack(mesh, times, keys, interpolation));
        }
    }

    private static void AttachNodeHierarchy(JsonElement root, SceneNode[] nodes)
    {
        JsonElement elements = RequireArray(root, "nodes");
        for (int parentIndex = 0; parentIndex < nodes.Length; parentIndex++)
        {
            if (!elements[parentIndex].TryGetProperty("children", out JsonElement children))
            {
                continue;
            }
            foreach (JsonElement childElement in AsArray(children, "node.children").EnumerateArray())
            {
                SceneNode child = At(nodes, ReadIndex(childElement, "child node"), "child node");
                if (child.Parent is not null)
                {
                    throw new InvalidDataException("A glTF node has more than one parent.");
                }
                try
                {
                    nodes[parentIndex].AddChild(child);
                }
                catch (InvalidOperationException exception)
                {
                    throw new InvalidDataException("The glTF node hierarchy contains a cycle.", exception);
                }
            }
        }
    }

    private static void ReadTransform(JsonElement source, Transform3D transform)
    {
        bool hasMatrix = source.TryGetProperty("matrix", out JsonElement matrixElement);
        if (hasMatrix && (source.TryGetProperty("translation", out _) ||
            source.TryGetProperty("rotation", out _) || source.TryGetProperty("scale", out _)))
        {
            throw new InvalidDataException("A glTF node cannot define both matrix and TRS transforms.");
        }
        if (hasMatrix)
        {
            float[] values = ReadFloatArray(matrixElement, 16, "node.matrix");
            Matrix4x4 matrix = new(
                values[0], values[1], values[2], values[3],
                values[4], values[5], values[6], values[7],
                values[8], values[9], values[10], values[11],
                values[12], values[13], values[14], values[15]);
            if (!Matrix4x4.Decompose(matrix, out Vector3 scale, out Quaternion rotation, out Vector3 translation))
            {
                throw new NotSupportedException("A glTF node matrix cannot be represented as Mu3D TRS.");
            }
            transform.Position = translation;
            transform.Rotation = rotation;
            transform.Scale = scale;
            return;
        }
        if (source.TryGetProperty("translation", out JsonElement translationElement))
        {
            transform.Position = ReadVector3(translationElement, "node.translation");
        }
        if (source.TryGetProperty("rotation", out JsonElement rotationElement))
        {
            Vector4 value = ReadVector4(rotationElement, "node.rotation");
            transform.Rotation = new Quaternion(value.X, value.Y, value.Z, value.W);
        }
        if (source.TryGetProperty("scale", out JsonElement scaleElement))
        {
            transform.Scale = ReadVector3(scaleElement, "node.scale");
        }
    }

    private static Vector3[] GenerateNormals(Vector3[] positions, uint[] indices)
    {
        Vector3[] normals = new Vector3[positions.Length];
        for (int index = 0; index < indices.Length; index += 3)
        {
            int a = checked((int)indices[index]);
            int b = checked((int)indices[index + 1]);
            int c = checked((int)indices[index + 2]);
            if ((uint)a >= (uint)positions.Length || (uint)b >= (uint)positions.Length ||
                (uint)c >= (uint)positions.Length)
            {
                throw new InvalidDataException("A glTF primitive index exceeds its POSITION count.");
            }
            Vector3 face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            if (face.LengthSquared() <= 1e-12f)
            {
                throw new InvalidDataException("A glTF triangle is degenerate and cannot generate a normal.");
            }
            normals[a] += face;
            normals[b] += face;
            normals[c] += face;
        }
        for (int index = 0; index < normals.Length; index++)
        {
            if (normals[index].LengthSquared() <= 1e-12f)
            {
                throw new InvalidDataException("A glTF vertex is not referenced by a valid triangle.");
            }
            normals[index] = Vector3.Normalize(normals[index]);
        }
        return normals;
    }

    private static Material CreateDefaultMaterial() => new PbrMaterial(
        new LinearRgba(1, 1, 1, 1, StandardColorSpaces.LinearSrgb),
        metallic: 1f,
        roughness: 1f,
        name: "glTF default material");

    private static T At<T>(T[] values, int index, string description) =>
        (uint)index < (uint)values.Length
            ? values[index]
            : throw new InvalidDataException($"glTF {description} index is out of range.");

    private static JsonElement RequireProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
            ? value
            : throw new InvalidDataException($"glTF requires '{name}'.");

    private static JsonElement RequireArray(JsonElement element, string name)
    {
        JsonElement value = RequireProperty(element, name);
        return AsArray(value, name);
    }

    private static JsonElement AsArray(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"glTF '{name}' must be an array.");
        }
        return value;
    }

    private static JsonElement RequireObject(JsonElement element, string name)
    {
        JsonElement value = RequireProperty(element, name);
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"glTF '{name}' must be an object.");
        }
        return value;
    }

    private static int ReadIndex(JsonElement element, string name) => ReadNonNegativeInt(element, name);

    private static int ReadNonNegativeInt(JsonElement element, string name)
    {
        if (!element.TryGetInt32(out int value) || value < 0)
        {
            throw new InvalidDataException($"glTF '{name}' must be a non-negative integer.");
        }
        return value;
    }

    private static float ReadUnitFloat(JsonElement element, string name)
    {
        float value = element.GetSingle();
        if (!float.IsFinite(value) || value is < 0f or > 1f)
        {
            throw new InvalidDataException($"glTF '{name}' must be finite and between zero and one.");
        }
        return value;
    }

    private static float ReadNonNegativeFloat(JsonElement element, string name)
    {
        float value = element.GetSingle();
        if (!float.IsFinite(value) || value < 0f)
        {
            throw new InvalidDataException($"glTF '{name}' must be finite and non-negative.");
        }
        return value;
    }

    private static float ReadFiniteFloat(JsonElement element, string name)
    {
        float value = element.GetSingle();
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException($"glTF '{name}' must be finite.");
        }
        return value;
    }

    private static string? ReadOptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
            ? value.GetString() ?? throw new InvalidDataException($"glTF '{name}' must be a string.")
            : null;

    private static bool ReadOptionalBoolean(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw new InvalidDataException($"glTF '{name}' must be a boolean."),
            }
            : false;

    private static Vector3 ReadVector3(JsonElement element, string name)
    {
        float[] values = ReadFloatArray(element, 3, name);
        return new Vector3(values[0], values[1], values[2]);
    }

    private static Vector4 ReadVector4(JsonElement element, string name)
    {
        float[] values = ReadFloatArray(element, 4, name);
        return new Vector4(values[0], values[1], values[2], values[3]);
    }

    private static Vector4 ReadUnitVector4(JsonElement element, string name)
    {
        Vector4 value = ReadVector4(element, name);
        if (value.X is < 0f or > 1f || value.Y is < 0f or > 1f ||
            value.Z is < 0f or > 1f || value.W is < 0f or > 1f)
        {
            throw new InvalidDataException($"glTF '{name}' values must be between zero and one.");
        }
        return value;
    }

    private static Vector3 ReadUnitVector3(JsonElement element, string name)
    {
        Vector3 value = ReadVector3(element, name);
        if (value.X is < 0f or > 1f || value.Y is < 0f or > 1f || value.Z is < 0f or > 1f)
        {
            throw new InvalidDataException($"glTF '{name}' values must be between zero and one.");
        }
        return value;
    }

    private static Vector3 ReadNonNegativeVector3(JsonElement element, string name)
    {
        Vector3 value = ReadVector3(element, name);
        if (value.X < 0f || value.Y < 0f || value.Z < 0f)
        {
            throw new InvalidDataException($"glTF '{name}' components must be non-negative.");
        }
        return value;
    }

    private static float[] ReadFloatArray(JsonElement element, int count, string name)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != count)
        {
            throw new InvalidDataException($"glTF '{name}' must contain {count} numbers.");
        }
        float[] values = new float[count];
        for (int index = 0; index < count; index++)
        {
            values[index] = element[index].GetSingle();
            if (!float.IsFinite(values[index]))
            {
                throw new InvalidDataException($"glTF '{name}' values must be finite.");
            }
        }
        return values;
    }

    private static float[] ReadFiniteFloatArray(JsonElement element, int count, string name)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != count)
        {
            throw new InvalidDataException($"glTF '{name}' must contain {count} numbers.");
        }
        float[] result = new float[count];
        for (int index = 0; index < count; index++)
        {
            result[index] = element[index].GetSingle();
            if (!float.IsFinite(result[index]))
            {
                throw new InvalidDataException($"glTF '{name}' values must be finite.");
            }
        }
        return result;
    }

    private readonly record struct TextureReference(
        int ImageIndex,
        MaterialTextureMapping Mapping);

    private readonly record struct EncodedImage(
        ReadOnlyMemory<byte> Data,
        string MimeType,
        string? Name);

    private readonly record struct ColorTextureSource(
        LinearRgbaImage? Decoded,
        CompressedMaterialTexture? Compressed);

    private readonly record struct DataTextureSource(
        NormalizedRgbaDataImage? Decoded,
        CompressedMaterialTexture? Compressed);

    private sealed class EncodedImageSet(
        EncodedImage[] images,
        IEncodedImageDecoder? decoder,
        IEncodedTextureTranscoder? transcoder,
        GraphicsCapabilities? capabilities)
    {
        private readonly ColorTextureSource?[] colorSourceCache =
            new ColorTextureSource?[images.Length];
        private readonly DataTextureSource?[] dataSourceCache =
            new DataTextureSource?[images.Length];
        private readonly LinearRgbaImage?[] colorCache = new LinearRgbaImage?[images.Length];
        private readonly NormalizedRgbaDataImage?[] dataCache =
            new NormalizedRgbaDataImage?[images.Length];

        internal ColorTextureSource DecodeColor(int index)
        {
            EncodedImage image = At(images, index, "image");
            if (colorSourceCache[index] is ColorTextureSource cached)
            {
                return cached;
            }
            CompressedMaterialTexture? compressed = TryTranscode(
                image,
                CompressedMaterialTextureContent.Color);
            ColorTextureSource result = compressed is not null
                ? new ColorTextureSource(null, compressed)
                : new ColorTextureSource(DecodeColorFallback(index), null);
            colorSourceCache[index] = result;
            return result;
        }

        internal DataTextureSource DecodeData(int index)
        {
            EncodedImage image = At(images, index, "image");
            if (dataSourceCache[index] is DataTextureSource cached)
            {
                return cached;
            }
            CompressedMaterialTexture? compressed = TryTranscode(
                image,
                CompressedMaterialTextureContent.Data);
            DataTextureSource result = compressed is not null
                ? new DataTextureSource(null, compressed)
                : new DataTextureSource(DecodeDataFallback(index), null);
            dataSourceCache[index] = result;
            return result;
        }

        internal LinearRgbaImage DecodeColorFallback(int index)
        {
            EncodedImage image = At(images, index, "image");
            return colorCache[index] ??= image.MimeType switch
            {
                "image/png" => PngImageReader.ReadSrgb(image.Data, image.Name),
                "image/jpeg" or "image/ktx2" => RequireDecoder().DecodeColor(
                    image.Data,
                    image.MimeType,
                    image.Name),
                _ => throw new NotSupportedException(
                    $"glTF color image MIME type '{image.MimeType}' is not supported."),
            };
        }

        internal NormalizedRgbaDataImage DecodeDataFallback(int index)
        {
            EncodedImage image = At(images, index, "image");
            return dataCache[index] ??= image.MimeType switch
            {
                "image/png" => PngImageReader.ReadData(image.Data, image.Name),
                "image/jpeg" or "image/ktx2" => RequireDecoder().DecodeData(
                    image.Data,
                    image.MimeType,
                    image.Name),
                _ => throw new NotSupportedException(
                    $"glTF data image MIME type '{image.MimeType}' is not supported."),
            };
        }

        private CompressedMaterialTexture? TryTranscode(
            EncodedImage image,
            CompressedMaterialTextureContent content)
        {
            if (image.MimeType != "image/ktx2" || transcoder is null)
            {
                return null;
            }
            CompressedMaterialTexture? result = transcoder.TryTranscode(
                image.Data,
                image.MimeType,
                content,
                capabilities!,
                image.Name);
            if (result is null)
            {
                return null;
            }
            if (result.Content != content)
            {
                throw new InvalidDataException(
                    "The encoded texture transcoder returned the wrong color/data semantic.");
            }
            if (!IsFormatSupported(result.Format, capabilities!))
            {
                throw new InvalidDataException(
                    "The encoded texture transcoder returned a format unsupported by the destination device.");
            }
            return result;
        }

        private static bool IsFormatSupported(
            GraphicsTextureFormat format,
            GraphicsCapabilities destination) => format switch
            {
                GraphicsTextureFormat.Bc1RgbaUnorm or
                GraphicsTextureFormat.Bc1RgbaUnormSrgb or
                GraphicsTextureFormat.Bc3RgbaUnorm or
                GraphicsTextureFormat.Bc3RgbaUnormSrgb or
                GraphicsTextureFormat.Bc7RgbaUnorm or
                GraphicsTextureFormat.Bc7RgbaUnormSrgb =>
                    destination.SupportsBcTextureCompression,
                GraphicsTextureFormat.Etc2Rgb8Unorm or
                GraphicsTextureFormat.Etc2Rgb8UnormSrgb or
                GraphicsTextureFormat.Etc2Rgba8Unorm or
                GraphicsTextureFormat.Etc2Rgba8UnormSrgb =>
                    destination.SupportsEtc2TextureCompression,
                GraphicsTextureFormat.Astc4x4Unorm or
                GraphicsTextureFormat.Astc4x4UnormSrgb or
                GraphicsTextureFormat.Astc6x6Unorm or
                GraphicsTextureFormat.Astc6x6UnormSrgb or
                GraphicsTextureFormat.Astc8x8Unorm or
                GraphicsTextureFormat.Astc8x8UnormSrgb =>
                    destination.SupportsAstcTextureCompression,
                _ => false,
            };

        private IEncodedImageDecoder RequireDecoder() => decoder ??
            throw new NotSupportedException(
                "This glTF contains an encoded image without a built-in decoder and requires an " +
                "IEncodedImageDecoder supplied by the application. Mu3D.Native.UltraHdr supplies " +
                "JpegImageDecoder with optional Ultra HDR gain-map support; KTX2 requires a " +
                "separate adapter.");
    }

    private sealed class ImportContext(JsonElement root, ReadOnlyMemory<byte>[] buffers)
    {
        private readonly HashSet<string> ignoredOptionalExtensions = new(StringComparer.Ordinal);

        internal JsonElement Root { get; } = root;

        internal IReadOnlyCollection<string> IgnoredOptionalExtensions => ignoredOptionalExtensions;

        internal void RecordIgnoredOptionalExtension(string extension) =>
            ignoredOptionalExtensions.Add(extension);

        internal Vector3[] ReadVector3Accessor(int accessorIndex, string semantic)
        {
            Accessor accessor = ReadAccessor(accessorIndex, semantic, "VEC3", 5126, 12);
            Vector3[] values = new Vector3[accessor.Count];
            for (int index = 0; index < values.Length; index++)
            {
                ReadOnlySpan<byte> item = accessor.Item(index);
                values[index] = new Vector3(ReadSingle(item), ReadSingle(item[4..]), ReadSingle(item[8..]));
            }
            return values;
        }

        internal Vector2[] ReadVector2Accessor(int accessorIndex, string semantic)
        {
            Accessor accessor = ReadAccessor(accessorIndex, semantic, "VEC2", 5126, 8);
            Vector2[] values = new Vector2[accessor.Count];
            for (int index = 0; index < values.Length; index++)
            {
                ReadOnlySpan<byte> item = accessor.Item(index);
                values[index] = new Vector2(ReadSingle(item), ReadSingle(item[4..]));
            }
            return values;
        }

        internal Vector4[] ReadVector4Accessor(int accessorIndex, string semantic)
        {
            Accessor accessor = ReadAccessor(accessorIndex, semantic, "VEC4", 5126, 16);
            Vector4[] values = new Vector4[accessor.Count];
            for (int index = 0; index < values.Length; index++)
            {
                ReadOnlySpan<byte> item = accessor.Item(index);
                values[index] = new Vector4(
                    ReadSingle(item),
                    ReadSingle(item[4..]),
                    ReadSingle(item[8..]),
                    ReadSingle(item[12..]));
            }
            return values;
        }

        internal Matrix4x4[] ReadMatrix4x4Accessor(int accessorIndex, string semantic)
        {
            Accessor accessor = ReadAccessor(accessorIndex, semantic, "MAT4", 5126, 64);
            Matrix4x4[] values = new Matrix4x4[accessor.Count];
            for (int index = 0; index < values.Length; index++)
            {
                ReadOnlySpan<byte> item = accessor.Item(index);
                values[index] = new Matrix4x4(
                    ReadSingle(item), ReadSingle(item[4..]), ReadSingle(item[8..]), ReadSingle(item[12..]),
                    ReadSingle(item[16..]), ReadSingle(item[20..]), ReadSingle(item[24..]), ReadSingle(item[28..]),
                    ReadSingle(item[32..]), ReadSingle(item[36..]), ReadSingle(item[40..]), ReadSingle(item[44..]),
                    ReadSingle(item[48..]), ReadSingle(item[52..]), ReadSingle(item[56..]), ReadSingle(item[60..]));
            }
            return values;
        }

        internal JointIndices4[] ReadJointIndicesAccessor(int accessorIndex)
        {
            JsonElement accessorElement = At(
                RequireArray(Root, "accessors").EnumerateArray().ToArray(),
                accessorIndex,
                "accessor");
            int componentType = ReadNonNegativeInt(
                RequireProperty(accessorElement, "componentType"),
                "componentType");
            int componentSize = componentType switch { 5121 => 1, 5123 => 2, _ => 0 };
            if (componentSize == 0 || ReadOptionalBoolean(accessorElement, "normalized"))
            {
                throw new NotSupportedException("glTF JOINTS_0 must use non-normalized unsigned bytes or shorts.");
            }
            Accessor accessor = ReadAccessor(
                accessorIndex,
                "JOINTS_0",
                "VEC4",
                componentType,
                4 * componentSize);
            JointIndices4[] values = new JointIndices4[accessor.Count];
            for (int index = 0; index < values.Length; index++)
            {
                ReadOnlySpan<byte> item = accessor.Item(index);
                values[index] = componentSize == 1
                    ? new JointIndices4(item[0], item[1], item[2], item[3])
                    : new JointIndices4(
                        BinaryPrimitives.ReadUInt16LittleEndian(item),
                        BinaryPrimitives.ReadUInt16LittleEndian(item[2..]),
                        BinaryPrimitives.ReadUInt16LittleEndian(item[4..]),
                        BinaryPrimitives.ReadUInt16LittleEndian(item[6..]));
            }
            return values;
        }

        internal float[] ReadFloatAccessor(int accessorIndex, string semantic)
        {
            Accessor accessor = ReadAccessor(accessorIndex, semantic, "SCALAR", 5126, 4);
            float[] values = new float[accessor.Count];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = ReadSingle(accessor.Item(index));
                if (!float.IsFinite(values[index]))
                {
                    throw new InvalidDataException($"glTF {semantic} values must be finite.");
                }
            }
            return values;
        }

        internal ReadOnlyMemory<byte> ReadBufferView(int viewIndex)
        {
            JsonElement view = At(
                RequireArray(Root, "bufferViews").EnumerateArray().ToArray(),
                viewIndex,
                "bufferView");
            int bufferIndex = ReadIndex(RequireProperty(view, "buffer"), "bufferView.buffer");
            ReadOnlyMemory<byte> buffer = At(buffers, bufferIndex, "buffer");
            int offset = view.TryGetProperty("byteOffset", out JsonElement offsetElement)
                ? ReadNonNegativeInt(offsetElement, "bufferView.byteOffset")
                : 0;
            int length = ReadNonNegativeInt(RequireProperty(view, "byteLength"), "bufferView.byteLength");
            if (offset > buffer.Length - length)
            {
                throw new InvalidDataException("A glTF bufferView exceeds its buffer bounds.");
            }
            return buffer.Slice(offset, length);
        }

        internal uint[] ReadIndexAccessor(int accessorIndex)
        {
            JsonElement accessorElement = At(RequireArray(Root, "accessors").EnumerateArray().ToArray(), accessorIndex, "accessor");
            int componentType = ReadNonNegativeInt(RequireProperty(accessorElement, "componentType"), "componentType");
            int size = componentType switch { 5121 => 1, 5123 => 2, 5125 => 4, _ => 0 };
            if (size == 0)
            {
                throw new NotSupportedException("glTF indices must use unsigned byte, short or int components.");
            }
            Accessor accessor = ReadAccessor(accessorIndex, "indices", "SCALAR", componentType, size);
            uint[] values = new uint[accessor.Count];
            for (int index = 0; index < values.Length; index++)
            {
                ReadOnlySpan<byte> item = accessor.Item(index);
                values[index] = size switch
                {
                    1 => item[0],
                    2 => BinaryPrimitives.ReadUInt16LittleEndian(item),
                    _ => BinaryPrimitives.ReadUInt32LittleEndian(item),
                };
            }
            return values;
        }

        private Accessor ReadAccessor(
            int accessorIndex,
            string semantic,
            string expectedType,
            int expectedComponentType,
            int elementSize)
        {
            JsonElement accessor = At(RequireArray(Root, "accessors").EnumerateArray().ToArray(), accessorIndex, "accessor");
            if (accessor.TryGetProperty("sparse", out _))
            {
                throw new NotSupportedException("Sparse glTF accessors are not supported by this slice.");
            }
            if (ReadOptionalString(accessor, "type") != expectedType ||
                ReadNonNegativeInt(RequireProperty(accessor, "componentType"), "componentType") != expectedComponentType)
            {
                throw new NotSupportedException($"glTF {semantic} uses an unsupported accessor representation.");
            }
            int count = ReadNonNegativeInt(RequireProperty(accessor, "count"), "accessor.count");
            if (count == 0)
            {
                throw new InvalidDataException("A mesh accessor cannot be empty.");
            }
            int viewIndex = ReadIndex(RequireProperty(accessor, "bufferView"), "accessor.bufferView");
            JsonElement view = At(RequireArray(Root, "bufferViews").EnumerateArray().ToArray(), viewIndex, "bufferView");
            int bufferIndex = ReadIndex(RequireProperty(view, "buffer"), "bufferView.buffer");
            ReadOnlyMemory<byte> buffer = At(buffers, bufferIndex, "buffer");
            int viewOffset = view.TryGetProperty("byteOffset", out JsonElement viewOffsetElement)
                ? ReadNonNegativeInt(viewOffsetElement, "bufferView.byteOffset")
                : 0;
            int viewLength = ReadNonNegativeInt(RequireProperty(view, "byteLength"), "bufferView.byteLength");
            int accessorOffset = accessor.TryGetProperty("byteOffset", out JsonElement accessorOffsetElement)
                ? ReadNonNegativeInt(accessorOffsetElement, "accessor.byteOffset")
                : 0;
            int stride = view.TryGetProperty("byteStride", out JsonElement strideElement)
                ? ReadNonNegativeInt(strideElement, "bufferView.byteStride")
                : elementSize;
            if (stride < elementSize || viewOffset > buffer.Length - viewLength ||
                accessorOffset > viewLength || checked((long)accessorOffset + (long)(count - 1) * stride + elementSize) > viewLength)
            {
                throw new InvalidDataException("A glTF accessor exceeds its bufferView bounds.");
            }
            return new Accessor(buffer.Slice(viewOffset + accessorOffset), count, stride, elementSize);
        }

        private static float ReadSingle(ReadOnlySpan<byte> bytes) =>
            BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes));
    }

    private readonly record struct Accessor(
        ReadOnlyMemory<byte> Data,
        int Count,
        int Stride,
        int ElementSize)
    {
        internal ReadOnlySpan<byte> Item(int index) => Data.Span.Slice(index * Stride, ElementSize);
    }

    private sealed record MeshDefinition(
        MeshGeometry[] Primitives,
        float[] Weights,
        IReadOnlyDictionary<int, int>[] MaterialVariants);

    private sealed record PunctualLightDefinition(
        string Type,
        LinearRgba Color,
        float Intensity,
        float Range,
        float InnerConeAngle,
        float OuterConeAngle,
        string? Name)
    {
        internal PunctualLight CreateInstance()
        {
            return Type switch
            {
                "directional" => new DirectionalLight(Color, Intensity, Name),
                "point" => new PointLight(Color, Intensity, Name) { Range = Range },
                "spot" => new SpotLight(Color, Intensity, Name)
                {
                    Range = Range,
                    OuterConeAngle = OuterConeAngle,
                    InnerConeAngle = InnerConeAngle,
                },
                _ => throw new InvalidOperationException("Validated punctual-light type was lost."),
            };
        }
    }

    private sealed record NodeImport(
        SceneNode[] Nodes,
        Mesh[][] Meshes,
        GltfMaterialVariantBinding[] MaterialVariantBindings);
}
