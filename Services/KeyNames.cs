namespace BestInScript.API.Services
{
    /// <summary>
    /// The canonical catalog of key names accepted in script steps, surfaced
    /// to the UI via GET /api/scripts/valid-keys. Every entry must resolve in
    /// <see cref="InputSimulatorService"/> (locked by a unit test).
    /// </summary>
    public static class KeyNames
    {
        public static IEnumerable<string> All()
        {
            var letters  = Enumerable.Range('A', 26).Select(c => ((char)c).ToString());
            var digits   = Enumerable.Range(0, 10).Select(n => n.ToString());
            var fkeys    = Enumerable.Range(1, 12).Select(n => $"F{n}");
            var numpad   = Enumerable.Range(0, 10).Select(n => $"NumPad{n}");
            var special  = new[]
            {
                "Space","Enter","Tab","Escape","Pause","Backspace","Delete","Insert",
                "Home","End","PageUp","PageDown",
                "Up","Down","Left","Right",
                "Shift","LShift","RShift",
                "Ctrl","LCtrl","RCtrl",
                "Alt","LAlt","RAlt",
                "Multiply","Add","Subtract","Decimal","Divide"
            };
            var mouse = new[] { "Mouse1","Mouse2","Mouse3","Mouse4","Mouse5" };

            return letters.Concat(digits).Concat(fkeys).Concat(numpad)
                          .Concat(special).Concat(mouse);
        }
    }
}
