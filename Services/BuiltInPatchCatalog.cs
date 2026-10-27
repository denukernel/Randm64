using System.IO;
using System.Text.RegularExpressions;

namespace Sm64DecompLevelViewer.Services;

public static class BuiltInPatchCatalog
{
    public static SourceEditPlan Prepare(string root, IEnumerable<string> selected)
    {
        var plan = new SourceEditPlan();
        foreach (string id in selected.Distinct())
        {
            switch (id)
            {
                case "skip-head": Redirect("levels/intro/script.c", "EXIT_AND_EXECUTE", "level_intro_mario_head_regular"); break;
                case "skip-title":
                    Redirect(File.Exists(Path.Combine(root,"levels","entry.c")) ? "levels/entry.c" : "levels/entry/script.c", "EXECUTE", "level_intro_splash_screen"); break;
                case "lives": Assignment("src/game/mario.c", @"(?<target>(?:m|gMarioState)\s*->\s*numLives)\s*=\s*4\s*;", @"(?:m|gMarioState)\s*->\s*numLives\s*=\s*99\s*;", "${target} = 99;"); break;
                case "stage-select": Assignment("src/game/main.c", @"gDebugLevelSelect\s*=\s*FALSE\s*;", @"gDebugLevelSelect\s*=\s*TRUE\s*;", "gDebugLevelSelect = TRUE;"); break;
                default: throw new InvalidDataException("retired or unknown built-in patch: " + id);
            }
        }
        return plan;
        string Read(string relative)
        {
            string path = Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar));
            if (!File.Exists(path)) throw new FileNotFoundException("patch target is missing",path);
            return plan.Read(path);
        }
        void Assignment(string relative,string oldPattern,string newPattern,string replacement)
        {
            string source=Read(relative), masked=CSourceCalls.MaskComments(source);
            var matches=Regex.Matches(masked,oldPattern);
            if (matches.Count==0 && Regex.Matches(masked,newPattern).Count==1) return;
            if (matches.Count!=1) throw new InvalidDataException("patch requires one recognized assignment; target is unsupported or ambiguous: "+relative);
            var match=matches[0];
            plan.Write(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)),source.Remove(match.Index,match.Length).Insert(match.Index,match.Result(replacement)));
        }
        void Redirect(string relative,string command,string oldEntry)
        {
            string source=Read(relative);
            var calls=CSourceCalls.Read(source,name=>name==command).Where(call=>call.Arguments.Count==4).ToList();
            var matches=calls.Where(call=>call.Argument(source,3)==oldEntry && call.Argument(source,1)=="_introSegmentRomStart" && call.Argument(source,2)=="_introSegmentRomEnd").ToList();
            if(matches.Count==0 && calls.Any(call=>call.Argument(source,3)=="level_main_menu_entry_1" && call.Argument(source,1)=="_menuSegmentRomStart" && call.Argument(source,2)=="_menuSegmentRomEnd")) return;
            if(matches.Count!=1) throw new InvalidDataException("patch requires one recognized level transition: "+relative);
            var call=matches[0];
            string replacement=CSourceCalls.ReplaceArguments(source,call,new(){[1]="_menuSegmentRomStart",[2]="_menuSegmentRomEnd",[3]="level_main_menu_entry_1"});
            source=source.Remove(call.Start,call.End-call.Start).Insert(call.Start,replacement);
            if(!source.Contains("\"levels/menu/header.h\"")) source="#include \"levels/menu/header.h\"\n"+source;
            plan.Write(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)),source);
        }
    }
}
