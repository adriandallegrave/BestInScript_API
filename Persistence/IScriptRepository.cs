using BestInScript.API.Models;

namespace BestInScript.API.Persistence
{
    /// <summary>Persistence seam for <see cref="ScriptConfig"/> storage.</summary>
    public interface IScriptRepository
    {
        List<ScriptConfig> GetAll();
        ScriptConfig? GetById(Guid id);
        ScriptConfig Save(ScriptConfig script);
        bool Delete(Guid id);
    }
}
