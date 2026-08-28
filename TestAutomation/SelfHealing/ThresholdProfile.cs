namespace SelfHealing
{
    /// <summary>
    /// Predefined threshold profiles balancing auto-healing recall against false-heal risk.
    /// </summary>
    public enum ThresholdProfile
    {
        /// <summary>
        /// Balanced operating point (~0.75 confidence threshold, 0.05 margin, 0.40 evidence).
        /// Delivers high recall while cutting false heals on removed controls substantially.
        /// </summary>
        Balanced,

        /// <summary>
        /// Conservative operating point (~0.90 confidence threshold, 0.08 margin, 0.50 evidence).
        /// Minimizes false heals and false-green tests, routing drifted controls to manual review.
        /// </summary>
        Conservative,

        /// <summary>
        /// Aggressive operating point (~0.50 confidence threshold, 0.03 margin, 0.30 evidence).
        /// Maximizes autonomous recall across compound and shifted elements with higher false-heal tolerance.
        /// </summary>
        Aggressive
    }
}
