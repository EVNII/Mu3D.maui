using System.Diagnostics;

namespace Mu3D.WgpuGen;

internal static class ClangSharpDriver
{
    public static async Task<string> GenerateAsync(
        string repositoryRoot,
        string webGpuHeaderPath,
        string wgpuHeaderPath,
        CancellationToken cancellationToken)
    {
        string temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            $"mu3d-clangsharp-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            string rawOutputPath = Path.Combine(temporaryDirectory, "WgpuNative.Raw.cs");
            string headerDirectory = Path.GetDirectoryName(Path.GetFullPath(wgpuHeaderPath))!;
            string portableIncludeDirectory = Path.Combine(
                repositoryRoot,
                "tools",
                "Mu3D.WgpuGen",
                "clang-include");

            ProcessStartInfo startInfo = new("dotnet")
            {
                WorkingDirectory = repositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            string[] arguments =
            [
                "tool", "run", "ClangSharpPInvokeGenerator", "--",
                "-f", Path.GetFullPath(wgpuHeaderPath),
                "-I", headerDirectory,
                "-I", portableIncludeDirectory,
                "--additional=-nostdinc",
                "-x", "c",
                "-std", "c11",
                "-n", "Mu3D.Native.Wgpu.Interop",
                "-o", rawOutputPath,
                "-l", "wgpu_native",
                "-m", "WgpuNative",
                "-c", "codegen=latest",
                "-c", "file=single",
                "-c", "types=unix",
                "--generate", "helper-types",
                "--generate", "disable-runtime-marshalling",
                "--generate", "enum-member-type-name=false",
                "--with-access-specifier", "*=Internal",
                "-t", Path.GetFullPath(wgpuHeaderPath),
                "-t", Path.GetFullPath(webGpuHeaderPath),
            ];

            foreach (string argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start the ClangSharp binding generator.");
            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            string output = await standardOutput.ConfigureAwait(false);
            string error = await standardError.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                string detail = string.IsNullOrWhiteSpace(error) ? output : error;
                throw new InvalidDataException(
                    "ClangSharp generation failed. Run 'dotnet tool restore' at the repository root. " +
                    detail.Trim());
            }

            return await File.ReadAllTextAsync(rawOutputPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }
}
