using OpenTK.Mathematics;

namespace Sm64DecompLevelViewer.Models;

// Preview transforms retain sub-unit precision and never enter the editable level object list.
public sealed record IntroActorPose(string ModelName, Vector3 Position, Vector3 Rotation, int AnimationFrame);
