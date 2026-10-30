using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Sm64DecompLevelViewer.Models;

namespace Sm64DecompLevelViewer.Services;

public sealed record ObjectiveInfo(string Kind, string Evidence, bool CanEditStar, string Requirements);
public static class ObjectiveCatalog
{
    public static string Kind(string behavior)=>behavior switch
    {
        "bhvStar" or "bhvStarSpawnCoordinates" => "placed star",
        "bhvHiddenRedCoinStar" or "bhvBowserCourseRedCoinStar" => "red coins",
        "bhvHiddenStar" => "secrets",
        "bhvKoopa" or "bhvRacingPenguin" => "race controller",
        "bhvKingBobomb" or "bhvWhompKingBoss" or "bhvBigBully" or "bhvBigBullyWithMinions" or "bhvEyerokBoss" or "bhvBigBoo" => "boss reward candidate",
        _ => "custom / source reference"
    };
    public static bool TryParameters(string text,out uint value) => text.StartsWith("0x",StringComparison.OrdinalIgnoreCase)
        ? uint.TryParse(text[2..],NumberStyles.HexNumber,CultureInfo.InvariantCulture,out value)
        : uint.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out value);
    public static ObjectiveInfo Describe(string root,ProjectObjectEntry entry,Func<string,string>? readSource=null)
    {
        readSource ??= file=>File.Exists(file) ? CSourceCalls.MaskComments(File.ReadAllText(file)) : "";
        string behavior=entry.Behavior;
        string path=Path.Combine(root,"src","game","behaviors","spawn_star.inc.c");
        string source=readSource(path);
        bool encoding=Regex.IsMatch(source,@"oBhvParams\s*>>\s*24") && source.Contains("starIndex");
        string kind=Kind(behavior);
        bool direct=behavior is "bhvStar" or "bhvStarSpawnCoordinates" or "bhvHiddenRedCoinStar";
        if(behavior=="bhvHiddenStar")
        {
            string hidden=Path.Combine(root,"src","game","behaviors","hidden_star.inc.c");
            direct=readSource(hidden).Contains("star->oBhvParams = o->oBhvParams");
        }
        string rewardFile=behavior switch
        {
            "bhvKoopa"=>"koopa.inc.c","bhvRacingPenguin"=>"racing_penguin.inc.c","bhvKingBobomb"=>"king_bobomb.inc.c",
            "bhvWhompKingBoss"=>"whomp.inc.c","bhvBigBully" or "bhvBigBullyWithMinions"=>"bully.inc.c","bhvEyerokBoss"=>"eyerok.inc.c","bhvBigBoo"=>"boo.inc.c",_=>""
        };
        string rewardPath=rewardFile.Length==0 ? "" : Path.Combine(root,"src","game","behaviors",rewardFile);
        string rewardSource=rewardPath.Length==0 ? "" : readSource(rewardPath);
        bool inherited=source.Contains("star->oBhvParams = o->oBhvParams");
        if(rewardSource.Length>0 && inherited && Regex.IsMatch(rewardSource,@"\bspawn_default_star\s*\(") && !Regex.IsMatch(rewardSource,@"o->oBhvParams\s*=")) direct=true;
        if(behavior=="bhvBowserCourseRedCoinStar") direct=inherited && readSource(Path.Combine(root,"src","game","behaviors","hidden_star.inc.c")).Contains("spawn_no_exit_star(");
        if(behavior=="bhvKoopa")
        {
            string constants=readSource(Path.Combine(root,"include","object_constants.h"));
            var match=Regex.Match(constants,@"#define\s+KOOPA_BP_KOOPA_THE_QUICK_BASE\s+(\d+)");
            direct=direct && match.Success && TryParameters(entry.Parameters,out uint koopa) && uint.TryParse(match.Groups[1].Value,out uint first) && ((koopa>>16)&255)>=first && ((koopa>>16)&255)<=first+1;
        }
        string hiddenPath=Path.Combine(root,"src","game","behaviors","hidden_star.inc.c");
        string hiddenSource=readSource(hiddenPath);
        string requirements=kind switch
        {
            "red coins" when source.Contains("oHiddenStarTriggerCounter == 8") || hiddenSource.Contains("oHiddenStarTriggerCounter == 8") => "source target: 8 red coins; candidates in this area may differ by act",
            "secrets" when hiddenSource.Contains("oHiddenStarTriggerCounter == 5") => "source target: 5 triggers; triggers use the nearest hidden-star controller during gameplay",
            "placed star" => "collect the placed star",
            _ => "requirements unresolved; inspect behavior source"
        };
        return new(kind,encoding ? path+(rewardPath.Length>0 ? "\nReward behavior: "+rewardPath : "") : "star encoding unverified in selected source",entry.Object.SourceType==ObjectSourceType.Normal && encoding && direct && TryParameters(entry.Parameters,out _),requirements);
    }
}
