using Xunit;

// GameLog keeps one shared ring buffer of the last 80 lines. Some tests assert on it (RoutineTests) while
// others write many lines (every simulated tug-of-war logs its end), so test classes must not run at the
// same time. The whole suite takes well under a second, so nothing is lost by running it serially.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
