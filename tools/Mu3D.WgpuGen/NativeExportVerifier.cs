using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Mu3D.WgpuGen;

internal static partial class NativeExportVerifier
{
    public static async Task<IReadOnlyList<string>> VerifyAsync(
        string generatedBindingsPath,
        string nativeLibraryPath,
        CancellationToken cancellationToken)
    {
        string generatedSource = await File.ReadAllTextAsync(
            generatedBindingsPath,
            cancellationToken).ConfigureAwait(false);
        HashSet<string> imports = ParseManagedImports(generatedSource);
        if (imports.Count != ClangSharpPostProcessor.ExpectedFunctionCount)
        {
            return
            [
                $"Expected {ClangSharpPostProcessor.ExpectedFunctionCount} generated wgpu imports, " +
                $"but found {imports.Count} in {generatedBindingsPath}.",
            ];
        }

        (string output, string? error) = await ReadNativeSymbolsAsync(
            nativeLibraryPath,
            cancellationToken).ConfigureAwait(false);
        if (error is not null)
        {
            return [error];
        }

        return Compare(imports, ParseNativeExports(output));
    }

    internal static HashSet<string> ParseManagedImports(string source)
    {
        return ManagedImportRegex()
            .Matches(source.ReplaceLineEndings("\n"))
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    internal static HashSet<string> ParseNativeExports(string symbolOutput)
    {
        return NativeExportRegex()
            .Matches(symbolOutput.ReplaceLineEndings("\n"))
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    internal static IReadOnlyList<string> Compare(
        IReadOnlySet<string> managedImports,
        IReadOnlySet<string> nativeExports)
    {
        string[] missing = managedImports.Except(nativeExports, StringComparer.Ordinal).Order().ToArray();
        string[] unexpected = nativeExports.Except(managedImports, StringComparer.Ordinal).Order().ToArray();
        List<string> errors = [];
        if (missing.Length != 0)
        {
            errors.Add($"Native library is missing generated exports: {string.Join(", ", missing)}");
        }

        if (unexpected.Length != 0)
        {
            errors.Add($"Native library has wgpu exports absent from generated bindings: {string.Join(", ", unexpected)}");
        }

        return errors;
    }

    private static async Task<(string Output, string? Error)> ReadNativeSymbolsAsync(
        string nativeLibraryPath,
        CancellationToken cancellationToken)
    {
        string fullPath = Path.GetFullPath(nativeLibraryPath);
        if (!File.Exists(fullPath))
        {
            return (string.Empty, $"Native library does not exist: {fullPath}");
        }

        if (Path.GetExtension(fullPath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return (string.Join('\n', PeExportReader.Read(fullPath)), null);
            }
            catch (BadImageFormatException exception)
            {
                return (string.Empty, $"Could not read PE exports from {fullPath}: {exception.Message}");
            }
        }

        ProcessStartInfo startInfo;
        if (!OperatingSystem.IsWindows())
        {
            string symbolTool = Environment.GetEnvironmentVariable("MU3D_NATIVE_SYMBOL_TOOL") ?? "nm";
            startInfo = new ProcessStartInfo(symbolTool);
            if (OperatingSystem.IsMacOS())
            {
                startInfo.ArgumentList.Add("-gU");
            }
            else if (Path.GetExtension(fullPath).Equals(".so", StringComparison.OrdinalIgnoreCase))
            {
                startInfo.ArgumentList.Add("-D");
                startInfo.ArgumentList.Add("--defined-only");
            }
            else
            {
                startInfo.ArgumentList.Add("-g");
                startInfo.ArgumentList.Add("--defined-only");
            }
        }
        else
        {
            return (string.Empty, $"No symbol reader is configured for {fullPath} on Windows.");
        }

        startInfo.ArgumentList.Add(fullPath);
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;

        try
        {
            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Failed to start symbol tool {startInfo.FileName}.");
            Task<string> output = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            string standardOutput = await output.ConfigureAwait(false);
            string standardError = await error.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                return (
                    standardOutput,
                    $"Symbol tool {startInfo.FileName} failed for {fullPath}: {standardError.Trim()}");
            }

            return (standardOutput, null);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or Win32Exception)
        {
            return (
                string.Empty,
                $"Could not inspect {fullPath} with {startInfo.FileName}: {exception.Message}");
        }
    }

    [GeneratedRegex(@"(?m)^\s*internal static partial .*?\b(wgpu[A-Za-z0-9_]+)\(")]
    private static partial Regex ManagedImportRegex();

    [GeneratedRegex(@"(?m)(?:^|\s)_?(wgpu[A-Za-z0-9_]+)\s*$")]
    private static partial Regex NativeExportRegex();
}
