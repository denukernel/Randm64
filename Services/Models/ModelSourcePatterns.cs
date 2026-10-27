using System.IO;
using System.Text.RegularExpressions;
using OpenTK.Mathematics;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services.Models;
internal static class ModelSourcePatterns
{
    internal static readonly Regex VtxArrayPattern = new(@"(?:static\s+)?(?:const\s+)?Vtx\s+(\w+)\[\]\s*=\s*\{", RegexOptions.Compiled);
    internal static readonly Regex VtxDataPattern = new(
        @"\{\{\{\s*(-?\d+),\s*(-?\d+),\s*(-?\d+)\},\s*\d+,\s*\{\s*(-?\d+),\s*(-?\d+)\},\s*\{(0x[0-9a-fA-F]{2}),\s*(0x[0-9a-fA-F]{2}),\s*(0x[0-9a-fA-F]{2}),\s*(0x[0-9a-fA-F]{2})\}\}\}",
        RegexOptions.Compiled);
    internal static readonly Regex SpVertexPattern = new(@"gsSPVertex\(\s*&?(\w+)(?:(?:\s*\+\s*|\s*\[\s*)(\d+)\s*\]?)?\s*,\s*(\d+)\s*,\s*(\d+)\s*\)", RegexOptions.Compiled);
    internal static readonly Regex Sp2TrianglesPattern = new(@"gsSP2Triangles\(\s*(\d+),\s*(\d+),\s*(\d+),\s*0x[0-9a-fA-F]+,\s*(\d+),\s*(\d+),\s*(\d+),\s*0x[0-9a-fA-F]+\)", RegexOptions.Compiled);
    internal static readonly Regex Sp1TrianglePattern = new(@"gsSP1Triangle\(\s*(\d+),\s*(\d+),\s*(\d+),\s*0x[0-9a-fA-F]+\)", RegexOptions.Compiled);
    internal static readonly Regex DisplayListPattern = new(@"const\s+Gfx\s+(\w+)\[\]\s*=\s*\{", RegexOptions.Compiled);
    internal static readonly Regex SetTextureImagePattern = new(@"gsDPSetTextureImage\(\s*[^,]+\s*,\s*[^,]+\s*,\s*[^,]+\s*,\s*(\w+)\)", RegexOptions.Compiled);
    internal static readonly Regex TextureSymbolPattern = new(@"(?:ALIGNED8\s+)?(?:static\s+)?const\s+(?:Texture|u8)\s+(\w+)\[\]\s*=\s*\{", RegexOptions.Compiled);
    internal static readonly Regex IncludePattern = new(@"#include\s+""([^""]+)""", RegexOptions.Compiled);
    internal static readonly Regex gdSPLightsPattern = new(
        @"(?:static\s+)?(?:const\s+)?Lights1\s+(\w+)\s*=\s*gdSPDefLights1\(\s*(0x[0-9a-fA-F]{2}|\d+),\s*(0x[0-9a-fA-F]{2}|\d+),\s*(0x[0-9a-fA-F]{2}|\d+),\s*(0x[0-9a-fA-F]{2}|\d+),\s*(0x[0-9a-fA-F]{2}|\d+),\s*(0x[0-9a-fA-F]{2}|\d+)",
        RegexOptions.Compiled);
    internal static readonly Regex SpLightPattern = new(@"gsSPLight\s*\(\s*&(\w+)\.(?:l(?:\[\d+\])?|a)\s*,\s*\d+\s*\)", RegexOptions.Compiled);
    internal static readonly Regex MovtexArrayPattern = new(@"(?:static\s+)?Movtex\s+(\w+)\[\]\s*=\s*\{", RegexOptions.Compiled);
    internal static readonly Regex MovtexTrisPattern = new(@"MOV_TEX_TRIS\s*\(\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)", RegexOptions.Compiled);
    internal static readonly Regex MovtexRotTrisPattern = new(@"MOV_TEX_ROT_TRIS\s*\(\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*,\s*(-?\d+)\s*\)", RegexOptions.Compiled);
    internal static readonly Regex MovtexBoxTrisPattern = new(@"MOV_TEX_4_BOX_TRIS\s*\(\s*(-?\d+)\s*,\s*(-?\d+)\s*\)", RegexOptions.Compiled);
}
