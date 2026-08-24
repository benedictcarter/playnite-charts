namespace PlayniteCharts.Controls
{
    /// <summary>
    /// Categorical palette, generated and verified with the data-viz validator
    /// (Machado-Oliveira-Fernandes CVD simulation, OKLab dE x100).
    ///
    /// A bubble plot needs the ALL-PAIRS gate, not the adjacent-pairs gate: any two
    /// categories can end up next to each other on the canvas, so every one of the
    /// 28 pairs has to be separable. Both modes clear it:
    ///
    ///   light  (surface #f5f5f5)  CVD dE 8.3   normal-vision dE 16.6
    ///   dark   (surface #151d38)  CVD dE 8.4   normal-vision dE 16.3
    ///
    /// Targets are CVD >= 8 and normal-vision >= 15. Three slots per mode sit below
    /// 3:1 contrast against the surface, which obliges the relief mechanisms - a
    /// legend is always drawn, the hover tooltip names the category, and the table
    /// view lists the same rows as text.
    ///
    /// Light and dark are the same eight hues (18, 66, 132, 180, 234, 252, 294, 324
    /// in OKLCH) stepped for their own surface, so slot identity survives a theme
    /// change. Slot order is fixed; the ninth category folds into a neutral "Other".
    /// Do not hand-edit a hex without re-running the validator - the set passes as a
    /// set, and single-colour tweaks break pairs elsewhere.
    /// </summary>
    internal static class PaletteData
    {
        //                                     blue       orange     green      magenta    teal       red        violet     deep blue
        internal static readonly string[] LightSeries = { "#57a8ff", "#f89700", "#477900", "#81168c", "#00bfa8", "#da4053", "#8962e4", "#007eae" };
        internal static readonly string[] DarkSeries = { "#0f90fe", "#c97a00", "#416f00", "#c55fcf", "#00a692", "#cf354b", "#6a3ebf", "#006992" };
    }
}
