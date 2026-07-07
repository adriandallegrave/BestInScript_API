namespace BestInScript.API.Models
{
    /// <summary>
    /// One step in a script sequence. Hold keys are pressed down first,
    /// then press keys are tapped (down+up), then hold keys are released.
    /// </summary>
    public class ScriptStep
    {
        /// <summary>Keys to hold down for the duration of this step.</summary>
        public List<string> Hold { get; set; } = [];

        /// <summary>Keys to tap (press and immediately release) during this step.</summary>
        public List<string> Press { get; set; } = [];
    }
}
