namespace BestInScript.API.Services
{
    /// <summary>
    /// Synthetic input seam. Production implementation is
    /// <see cref="InputSimulatorService"/> (Win32 SendInput with scan codes);
    /// tests substitute a recording fake.
    /// </summary>
    public interface IInputSimulator
    {
        void KeyPress(string key);
        void KeyDown(string key);
        void KeyUp(string key);
    }
}
