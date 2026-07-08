namespace BestInScript.API.Services
{
    /// <summary>
    /// Screen pixel sampling seam. Production implementation is
    /// <see cref="ScreenColorService"/> (passive GDI reads); tests substitute
    /// a fake with scripted colors.
    /// </summary>
    public interface IScreenSampler
    {
        (int R, int G, int B)? ColorAt(int x, int y);
        (int R, int G, int B)? ColorAtAveraged(int x, int y, int radius);
        (int X, int Y)? CursorPosition();
    }
}
