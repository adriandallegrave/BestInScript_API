using BestInScript.API.Models;

namespace BestInScript.API.Persistence
{
    /// <summary>Persistence seam for <see cref="Preset"/> storage.</summary>
    public interface IPresetRepository
    {
        List<Preset> GetAll();
        Preset? GetById(Guid id);
        Preset Save(Preset preset);
        bool Delete(Guid id);
    }
}
