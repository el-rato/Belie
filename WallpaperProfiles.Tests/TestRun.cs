using Xunit;

// The window tests share one WPF application, theme and desktop, so nothing runs beside them.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
