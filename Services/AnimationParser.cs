using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services
{
    public class MarioAnimation
    {
        public string Name { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int FrameCount { get; set; } = 1;
        public ushort[] Indices { get; set; } = Array.Empty<ushort>();
        public short[] Values { get; set; } = Array.Empty<short>();
    }

    public class AnimationParser
    {
        private static readonly Regex IndicesRegex = new(@"const\s+u16\s+\w+_indices\[\]\s*=\s*\{([\s\S]*?)\};", RegexOptions.Compiled);
        private static readonly Regex ValuesRegex = new(@"const\s+s16\s+\w+_values\[\]\s*=\s*\{([\s\S]*?)\};", RegexOptions.Compiled);

        public List<MarioAnimation> ScanAnimations(string projectRoot)
        {
            var animList = new List<MarioAnimation>();
            string animsPath = Path.Combine(projectRoot, "assets", "anims");
            if (!Directory.Exists(animsPath)) return animList;

            var files = Directory.GetFiles(animsPath, "anim_*.inc.c");
            foreach (var file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file).Replace(".inc", "");
                animList.Add(new MarioAnimation
                {
                    Name = name,
                    FilePath = file
                });
            }

            return animList;
        }

        public void LoadAnimationData(MarioAnimation anim)
        {
            if (!File.Exists(anim.FilePath)) return;

            try
            {
                string content = File.ReadAllText(anim.FilePath);

                // Clean comments
                content = Regex.Replace(content, @"/\*.*?\*/", "");
                content = Regex.Replace(content, @"//.*?\n", "\n");

                var indicesMatch = IndicesRegex.Match(content);
                var valuesMatch = ValuesRegex.Match(content);

                if (indicesMatch.Success && valuesMatch.Success)
                {
                    anim.Indices = ParseUShortArray(indicesMatch.Groups[1].Value);
                    anim.Values = ParseShortArray(valuesMatch.Groups[1].Value);

                    if (anim.Indices.Length > 0)
                    {
                        anim.FrameCount = anim.Indices[0]; // First element is the frame length of the root translation
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading animation {anim.Name}: {ex.Message}");
            }
        }

        private ushort[] ParseUShortArray(string text)
        {
            var parts = text.Split(new[] { ',', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<ushort>();
            foreach (var p in parts)
            {
                string clean = p.Trim();
                if (string.IsNullOrEmpty(clean)) continue;

                try
                {
                    if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add(Convert.ToUInt16(clean, 16));
                    }
                    else
                    {
                        list.Add(ushort.Parse(clean));
                    }
                }
                catch { }
            }
            return list.ToArray();
        }

        private short[] ParseShortArray(string text)
        {
            var parts = text.Split(new[] { ',', '\n', '\r', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<short>();
            foreach (var p in parts)
            {
                string clean = p.Trim();
                if (string.IsNullOrEmpty(clean)) continue;

                try
                {
                    if (clean.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    {
                        list.Add((short)Convert.ToInt32(clean, 16));
                    }
                    else
                    {
                        list.Add(short.Parse(clean));
                    }
                }
                catch { }
            }
            return list.ToArray();
        }

        public Dictionary<int, Matrix4> GetFrameTransforms(MarioAnimation anim, int frame, List<GeoNode> joints)
        {
            var transforms = new Dictionary<int, Matrix4>();
            if (anim.Indices.Length == 0 || anim.Values.Length == 0 || joints.Count == 0) return transforms;

            int indexPtr = 0;
            var parentMatrixStack = new Stack<Matrix4>();
            var worldMatrices = new Dictionary<int, Matrix4>();

            for (int i = 0; i < joints.Count; i++)
            {
                var joint = joints[i];
                Vector3 translation = joint.Translation;
                Vector3 rotation = Vector3.Zero;

                if (i == 0)
                {
                    // Root joint has 6 channels (tx, ty, tz, rx, ry, rz)
                    if (indexPtr + 11 < anim.Indices.Length)
                    {
                        translation.X = GetChannelValue(anim.Indices[indexPtr + 0], anim.Indices[indexPtr + 1], frame, anim.Values);
                        translation.Y = GetChannelValue(anim.Indices[indexPtr + 2], anim.Indices[indexPtr + 3], frame, anim.Values);
                        translation.Z = GetChannelValue(anim.Indices[indexPtr + 4], anim.Indices[indexPtr + 5], frame, anim.Values);

                        rotation.X = GetChannelValue(anim.Indices[indexPtr + 6], anim.Indices[indexPtr + 7], frame, anim.Values);
                        rotation.Y = GetChannelValue(anim.Indices[indexPtr + 8], anim.Indices[indexPtr + 9], frame, anim.Values);
                        rotation.Z = GetChannelValue(anim.Indices[indexPtr + 10], anim.Indices[indexPtr + 11], frame, anim.Values);

                        indexPtr += 12;
                    }
                }
                else
                {
                    // Child joints have 3 channels (rx, ry, rz)
                    if (indexPtr + 5 < anim.Indices.Length)
                    {
                        rotation.X = GetChannelValue(anim.Indices[indexPtr + 0], anim.Indices[indexPtr + 1], frame, anim.Values);
                        rotation.Y = GetChannelValue(anim.Indices[indexPtr + 2], anim.Indices[indexPtr + 3], frame, anim.Values);
                        rotation.Z = GetChannelValue(anim.Indices[indexPtr + 4], anim.Indices[indexPtr + 5], frame, anim.Values);

                        indexPtr += 6;
                    }
                }

                // Apply rotation scaling (convert units to radians: angle / 65536 * 360 degrees = angle * Math.PI / 32768)
                float rotX = (float)(rotation.X * Math.PI / 32768.0);
                float rotY = (float)(rotation.Y * Math.PI / 32768.0);
                float rotZ = (float)(rotation.Z * Math.PI / 32768.0);

                var translationMat = Matrix4.CreateTranslation(translation);
                var rotationMat = Matrix4.CreateRotationZ(rotZ) * Matrix4.CreateRotationX(rotX) * Matrix4.CreateRotationY(rotY);
                var jointLocalMat = rotationMat * translationMat;

                // Calculate parent matrix
                Matrix4 parentMat = Matrix4.Identity;
                if (joint.ParentIndex != -1 && worldMatrices.TryGetValue(joint.ParentIndex, out var pMat))
                {
                    parentMat = pMat;
                }

                Matrix4 worldMat = jointLocalMat * parentMat;
                worldMatrices[i] = worldMat;
                transforms[i] = worldMat;
            }

            return transforms;
        }

        private short GetChannelValue(ushort length, ushort offset, int frame, short[] values)
        {
            if (length == 0 || values.Length == 0) return 0;
            if (length == 1)
            {
                if (offset < values.Length) return values[offset];
                return 0;
            }

            int index = offset + Math.Min(frame, length - 1);
            if (index < values.Length) return values[index];
            return 0;
        }
    }
}
