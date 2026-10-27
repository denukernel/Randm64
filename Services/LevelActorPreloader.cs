using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

// Source parsing runs on one worker; OpenGL uploads remain on the viewport thread.
internal sealed class LevelActorPreloader : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly Dictionary<string, Task<(VisualMesh Mesh, MarioAnimation? Animation)?>> _requests = new();
    private readonly Task<LevelActorCatalog> _catalog;
    private Task _tail;

    public LevelActorPreloader(string root, string directory)
    {
        _catalog = Task.Run(() => new LevelActorCatalog(root, directory), _stop.Token);
        _tail = _catalog;
    }

    public bool TryGet(string model, out (VisualMesh Mesh, MarioAnimation? Animation)? result)
    {
        result = null;
        if (model == "MODEL_NONE") return true;
        if (!_requests.TryGetValue(model, out var task))
        {
            var previous = _tail;
            var token = _stop.Token;
            task = Task.Run(async () =>
            {
                try { await previous.ConfigureAwait(false); }
                catch { /* A failed actor must not block later requests. */ }
                token.ThrowIfCancellationRequested();
                var catalog = await _catalog.ConfigureAwait(false);
                return catalog.Resolve(model);
            }, token);
            _requests[model] = task;
            _tail = task;
        }
        if (!task.IsCompleted) return false;
        if (task.IsCompletedSuccessfully) result = task.Result;
        else if (task.IsFaulted) _ = task.Exception;
        return true;
    }

    public void Dispose() => _stop.Cancel();
}
