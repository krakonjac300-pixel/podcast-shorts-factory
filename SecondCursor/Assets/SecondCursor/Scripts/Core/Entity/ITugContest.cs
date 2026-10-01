namespace SecondCursor.Core.Entity
{
    /// <summary>
    /// Phase P: what the runtime reads from one tug-of-war, whichever model plays it (<see cref="TugOfWar"/>, the Phase N speed
    /// model, or <see cref="TugReel"/>, "haul it to the bin").
    /// </summary>
    public interface ITugContest
    {
        TugOutcome Outcome { get; }
        bool IsOver { get; }
        float Elapsed { get; }
        float ActiveElapsed { get; }
        bool InReady { get; }
        bool InReadGrace { get; }
        Vec2 ObjectPosition { get; }
        float PlayerLead { get; }
        float FinalLead { get; }
        bool KeepsOnRelease { get; }
        float Effort { get; }
        float PeakEffort { get; }
        float Strain { get; }
        TugOutcome Step(float dt, Vec2 player, bool holding, Vec2 entity, float grip);
    }
}
