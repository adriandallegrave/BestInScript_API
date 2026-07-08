using BestInScript.API.Models;

namespace BestInScript.API.Engine
{
    /// <summary>Runtime state of one registered preset.</summary>
    public sealed class PresetEntry(Preset config)
    {
        public Preset Config { get; } = config;

        /// <summary>True while this preset's trigger has been pressed an odd number of times.</summary>
        public bool IsActive { get; set; }
    }
}
