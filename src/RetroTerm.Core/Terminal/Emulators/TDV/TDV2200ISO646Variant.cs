namespace RetroTerm.Core.Terminal.Emulators.TDV
{
    /// <summary>
    /// Character set 1 variants for TDV terminals.
    /// Per TDV2115 spec section 9.1.
    /// </summary>
    public enum TDV2200ISO646Variant
    {
        /// <summary>
        /// International (US ASCII) - TDV2115 spec 9.1.1
        /// </summary>
        International = 0,

        /// <summary>
        /// Norwegian - TDV2115 spec 9.1.2
        /// </summary>
        Norwegian = 1,

        /// <summary>
        /// Swedish - TDV2115 spec 9.1.3
        /// </summary>
        Swedish = 2,

        /// <summary>
        /// German - TDV2115 spec 9.1.4
        /// </summary>
        German = 3
    }
}
