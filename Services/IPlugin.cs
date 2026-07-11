namespace Sm64DecompLevelViewer.Services
{
    public interface IPlugin
    {
        string Name { get; }
        string Description { get; }
        void Initialize(string projectRoot);
        void Execute();
    }
}
