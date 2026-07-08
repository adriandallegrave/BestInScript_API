using BestInScript.API.Models;
using BestInScript.API.Services;

namespace BestInScript.API.Engine
{
    /// <summary>
    /// The pixel-gate "ready" decision, kept as one pure function so it can be
    /// unit-tested and never accidentally simplified.
    /// </summary>
    public static class PixelReadyEvaluator
    {
        /// <summary>
        /// "Ready" means: close enough to the ready color in absolute terms,
        /// AND closer to ready than to cooldown. The second test is what makes
        /// a two-color trigger robust to the lighting/animation drift a single
        /// threshold can't handle. Do NOT collapse this to one comparison.
        /// </summary>
        public static bool IsReady((int R, int G, int B) sample, PixelTrigger pt)
        {
            var live = new[] { sample.R, sample.G, sample.B };
            double dReady = ScreenColorService.Distance(live, pt.ReadyColor);
            double dCool = ScreenColorService.Distance(live, pt.CooldownColor);

            return dReady <= pt.Tolerance && dReady < dCool;
        }
    }
}
