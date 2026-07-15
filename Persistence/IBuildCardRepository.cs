using BestInScript.API.Models;

namespace BestInScript.API.Persistence
{
    /// <summary>
    /// Persistence seam for <see cref="BuildCard"/> storage — the JSON rows and the
    /// image files that go with them.
    /// </summary>
    public interface IBuildCardRepository
    {
        /// <summary>All cards in cycle order (ascending Order, then Name).</summary>
        List<BuildCard> GetAll();

        BuildCard? GetById(Guid id);

        BuildCard Save(BuildCard card);

        /// <summary>Removes the row and its image file.</summary>
        bool Delete(Guid id);

        /// <summary>
        /// Absolute path of a card's image, or null when the card has no image or the
        /// file is missing. Always derived from the card's server-generated file name.
        /// </summary>
        string? ImagePath(BuildCard card);

        /// <summary>
        /// Writes image bytes for a card, replacing any previous image, and returns the
        /// bare file name to store on the row. The name is derived from
        /// <paramref name="id"/> — never from client input.
        /// </summary>
        Task<string> SaveImageAsync(Guid id, Stream content, string extension, CancellationToken ct = default);
    }
}
