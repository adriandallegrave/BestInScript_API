namespace BestInScript.API.Models
{
    /// <summary>Runtime status of a preset, surfaced to the UI and overlay.</summary>
    public class PresetStatus
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string TriggerKey { get; set; } = "";
        public bool IsActive { get; set; }
        public bool ShowInOverlay { get; set; }
        public List<Guid> ScriptIds { get; set; } = [];
    }
}
