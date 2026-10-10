namespace SecondCursor.Core.Story
{
    public enum BeatRecoveryStep { RestartBeat, SkipBeat, ToTitle }

    /// <summary>
    /// What a night does when a story beat throws: the first failure of a beat restarts it (the world is rebuilt for it, as for a jump), the
    /// second moves on to the next beat, and the last beat failing twice leaves for the title (the checkpoint stays, so Continue resumes).
    /// </summary>
    public sealed class BeatRecovery
    {
        readonly int[] _faults;

        public BeatRecovery(int beatCount)
        {
            _faults = new int[beatCount];
        }

        public BeatRecoveryStep OnFault(int beat)
        {
            if (beat < 0 || beat >= _faults.Length) return BeatRecoveryStep.ToTitle;
            if (++_faults[beat] == 1) return BeatRecoveryStep.RestartBeat;
            return beat < _faults.Length - 1 ? BeatRecoveryStep.SkipBeat : BeatRecoveryStep.ToTitle;
        }
    }
}
