using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Sm64DecompLevelViewer.Models;
using Sm64DecompLevelViewer.Services;

namespace Sm64DecompLevelViewer.Services.Levels;
internal static class MeshPlacementContext
{
        internal static (EditableGeometry Terrain,CollisionMesh Ground) Load(string _projectRoot, string _levelPath, Dictionary<string, string> _collisionFiles, CollisionParser _collisionParser, string area,string? excluded=null)
        {
            int index=int.Parse(System.Text.RegularExpressions.Regex.Match(area,@"\d+").Value);
            var scene=LevelSceneLoader.Load(_projectRoot,_levelPath,index);
            var terrain=ModelPlacementService.FromVisual(scene,t=>excluded==null ||
                scene.Vertices[t.V1].SourceArrayName?.StartsWith(excluded+"_v",StringComparison.Ordinal)!=true);
            var collision=_collisionParser.ParseCollisionFile(_collisionFiles[area],area,"") ?? throw new InvalidDataException("No collision geometry for placement.");
            return (terrain,collision);
        }
}
