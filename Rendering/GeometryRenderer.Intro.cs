using Sm64DecompLevelViewer.Models;
using OpenTK.Mathematics;

namespace Sm64DecompLevelViewer.Rendering;

public partial class GeometryRenderer
{
    public volatile bool ShowIntroActors;
    public volatile IntroActorPose[] IntroPreviewActors = Array.Empty<IntroActorPose>();
    private sealed record CutsceneFrame(Vector3 Position, Vector3 Focus, IntroActorPose[] Actors);
    private CutsceneFrame? _pendingCutsceneFrame;
    private int _stopCutsceneRequested;

    public void SubmitCutsceneFrame(Vector3 position, Vector3 focus, IntroActorPose[] actors)
        => Interlocked.Exchange(ref _pendingCutsceneFrame, new CutsceneFrame(position, focus, actors));

    private void ApplyCutsceneFrame()
    {
        var frame = Interlocked.Exchange(ref _pendingCutsceneFrame, null);
        if (frame != null)
        {
            SetCameraPositionAndTarget(frame.Position, frame.Focus);
            IntroPreviewActors = frame.Actors;
            IsPlayingPreview = true;
        }
        if (Interlocked.Exchange(ref _stopCutsceneRequested, 0) != 0)
            IsPlayingPreview = false;
    }
}
