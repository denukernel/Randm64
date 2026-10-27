namespace Sm64DecompLevelViewer.Models.Levels;
        public class ModelVertexViewModel : Sm64DecompLevelViewer.Models.Levels.IMeshPosition
{
            public int Index { get; set; }
            public int X { get; set; }
            public int Y { get; set; }
            public int Z { get; set; }
            public int S { get; set; }
            public int T { get; set; }
            public byte NX { get; set; }
            public byte NY { get; set; }
            public byte NZ { get; set; }
            public byte Alpha { get; set; }
        }
