using Mu3D.Rendering;

namespace Mu3D.Maui.Toolkit.Controls;

internal sealed class RenderOutputToolState(
    Func<RenderOutputId> readOutput,
    Action<RenderOutputId> writeOutput)
{
    private RenderOutputId previousOutput;
    private RenderOutputId appliedOutput;
    private bool ownsOutput;

    internal void Update(bool isEnabled, RenderOutputId output)
    {
        if (!output.IsValid)
        {
            throw new ArgumentException("A render-output identifier must be initialized.", nameof(output));
        }

        if (!isEnabled)
        {
            Release();
            return;
        }

        if (!ownsOutput)
        {
            previousOutput = readOutput();
            ownsOutput = true;
        }

        appliedOutput = output;
        if (readOutput() == output)
        {
            return;
        }

        writeOutput(output);
    }

    internal void Release()
    {
        if (!ownsOutput)
        {
            return;
        }

        ownsOutput = false;
        RenderOutputId currentOutput = readOutput();
        if (currentOutput != appliedOutput || currentOutput == previousOutput)
        {
            return;
        }

        writeOutput(previousOutput);
    }
}
