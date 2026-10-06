using Xunit;

// Each fixture boots a complete host; running them one after another keeps the tests fast and the timings stable.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
