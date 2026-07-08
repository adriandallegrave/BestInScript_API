namespace BestInScript.API.Engine
{
    /// <summary>
    /// Randomness seam for the humanlike delay jitter. Production forwards to
    /// <see cref="Random.Shared"/>; tests substitute a scripted sequence to
    /// pin the delay formula's bounds.
    /// </summary>
    public interface IRandomSource
    {
        double NextDouble();
    }

    public sealed class SharedRandomSource : IRandomSource
    {
        public double NextDouble() => Random.Shared.NextDouble();
    }
}
