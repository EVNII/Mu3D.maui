namespace Mu3D.Maui.Toolkit.Controls;

/// <summary>Identifies one portable key accepted by viewport keyboard adapters.</summary>
public enum ViewportKey
{
    /// <summary>Disables this binding.</summary>
    None,
    /// <summary>The left arrow key.</summary>
    LeftArrow,
    /// <summary>The right arrow key.</summary>
    RightArrow,
    /// <summary>The up arrow key.</summary>
    UpArrow,
    /// <summary>The down arrow key.</summary>
    DownArrow,
    /// <summary>The Page Up key.</summary>
    PageUp,
    /// <summary>The Page Down key.</summary>
    PageDown,
    /// <summary>The numeric keypad add key.</summary>
    Add,
    /// <summary>The numeric keypad subtract key.</summary>
    Subtract,
    /// <summary>The A key.</summary>
    A,
    /// <summary>The B key.</summary>
    B,
    /// <summary>The C key.</summary>
    C,
    /// <summary>The D key.</summary>
    D,
    /// <summary>The E key.</summary>
    E,
    /// <summary>The F key.</summary>
    F,
    /// <summary>The G key.</summary>
    G,
    /// <summary>The H key.</summary>
    H,
    /// <summary>The I key.</summary>
    I,
    /// <summary>The J key.</summary>
    J,
    /// <summary>The K key.</summary>
    K,
    /// <summary>The L key.</summary>
    L,
    /// <summary>The M key.</summary>
    M,
    /// <summary>The N key.</summary>
    N,
    /// <summary>The O key.</summary>
    O,
    /// <summary>The P key.</summary>
    P,
    /// <summary>The Q key.</summary>
    Q,
    /// <summary>The R key.</summary>
    R,
    /// <summary>The S key.</summary>
    S,
    /// <summary>The T key.</summary>
    T,
    /// <summary>The U key.</summary>
    U,
    /// <summary>The V key.</summary>
    V,
    /// <summary>The W key.</summary>
    W,
    /// <summary>The X key.</summary>
    X,
    /// <summary>The Y key.</summary>
    Y,
    /// <summary>The Z key.</summary>
    Z,
}

internal static class ViewportKeyboardBindingState
{
    internal static ViewportKeyboardAction? Resolve(
        ViewportKey key,
        ViewportKey rotateLeft,
        ViewportKey rotateRight,
        ViewportKey rotateUp,
        ViewportKey rotateDown,
        ViewportKey panLeft,
        ViewportKey panRight,
        ViewportKey panUp,
        ViewportKey panDown,
        ViewportKey dollyIn,
        ViewportKey dollyOut,
        ViewportKey alternateDollyIn,
        ViewportKey alternateDollyOut)
    {
        if (!Enum.IsDefined(key))
        {
            throw new ArgumentOutOfRangeException(nameof(key));
        }
        if (key == ViewportKey.None)
        {
            return null;
        }

        if (key == rotateLeft)
        {
            return ViewportKeyboardAction.RotateLeft;
        }
        if (key == rotateRight)
        {
            return ViewportKeyboardAction.RotateRight;
        }
        if (key == rotateUp)
        {
            return ViewportKeyboardAction.RotateUp;
        }
        if (key == rotateDown)
        {
            return ViewportKeyboardAction.RotateDown;
        }
        if (key == panLeft)
        {
            return ViewportKeyboardAction.PanLeft;
        }
        if (key == panRight)
        {
            return ViewportKeyboardAction.PanRight;
        }
        if (key == panUp)
        {
            return ViewportKeyboardAction.PanUp;
        }
        if (key == panDown)
        {
            return ViewportKeyboardAction.PanDown;
        }
        if (key == dollyIn || key == alternateDollyIn)
        {
            return ViewportKeyboardAction.DollyIn;
        }
        if (key == dollyOut || key == alternateDollyOut)
        {
            return ViewportKeyboardAction.DollyOut;
        }
        return null;
    }
}
