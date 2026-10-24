// Keep native CPU concurrency bounded; replays share a class and run sequentially.
[assembly: Xunit.CollectionBehavior(MaxParallelThreads = 4)]
