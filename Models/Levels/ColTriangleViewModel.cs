namespace Sm64DecompLevelViewer.Models.Levels;
        public class ColTriangleViewModel
        {
            public int Index { get; set; }
            public int V1 { get; set; }
            public int V2 { get; set; }
            public int V3 { get; set; }
            public string SurfaceType { get; set; } = "SURFACE_DEFAULT";
            public int? SpecialParam { get; set; }
        }
