// Each test provisions SQL Server and some tests also own the broker. Tests that exercise concurrency
// start overlapping operations explicitly within a disposable database.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
