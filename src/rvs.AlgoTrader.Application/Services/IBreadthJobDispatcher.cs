namespace rvs.AlgoTrader.Application.Services;

/// <summary>
/// Dispatches the breadth computation job to the background job queue.
/// Infrastructure implementation uses Hangfire; tests can stub this.
/// </summary>
public interface IBreadthJobDispatcher
{
    /// <summary>Enqueues the breadth computation job and returns the job ID.</summary>
    string Enqueue();
}
