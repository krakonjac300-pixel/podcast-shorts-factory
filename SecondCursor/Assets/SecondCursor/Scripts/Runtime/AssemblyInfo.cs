using System.Runtime.CompilerServices;

// The editor test bridge sets test-only hooks (save folder override, simulated Steam Deck, store dry run).
[assembly: InternalsVisibleTo("SecondCursor.Editor")]
