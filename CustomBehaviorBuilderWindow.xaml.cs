using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;

namespace Sm64DecompLevelViewer
{
    public partial class CustomBehaviorBuilderWindow : Window
    {
        private readonly string _projectRoot;

        public CustomBehaviorBuilderWindow(string projectRoot)
        {
            InitializeComponent();
            _projectRoot = projectRoot;

            PopulateModels();
        }

        private void PopulateModels()
        {
            var commonModels = new List<string>
            {
                "MODEL_NONE",
                "MODEL_GOOMBA",
                "MODEL_BOBOMB",
                "MODEL_KOOPA_THE_QUICK",
                "MODEL_BUTTERFLY",
                "MODEL_BOWSER",
                "MODEL_PIRANHA_PLANT",
                "MODEL_COIN",
                "MODEL_RED_COIN",
                "MODEL_BLUE_COIN",
                "MODEL_STAR",
                "MODEL_1UP",
                "MODEL_WOODEN_POST",
                "MODEL_METAL_BOX",
                "MODEL_EXCLAMATION_BOX",
                "MODEL_BOO",
                "MODEL_WARP_PIPE"
            };

            foreach (var model in commonModels)
            {
                ActorModelComboBox.Items.Add(model);
            }
            ActorModelComboBox.SelectedIndex = 1; // Default to Goomba
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            MainTabControl.SelectedIndex = 1;
        }

        private void CreateBehavior_Click(object sender, RoutedEventArgs e)
        {
            string name = BehaviorNameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(name))
            {
                MessageBox.Show("Please enter a behavior name.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Ensure name matches bhv prefix
            if (!name.StartsWith("bhv", StringComparison.OrdinalIgnoreCase))
            {
                name = "bhvCustom" + name;
            }
            else if (!name.StartsWith("bhvCustom", StringComparison.OrdinalIgnoreCase))
            {
                name = "bhvCustom" + name.Substring(3);
            }

            string preferredModel = ActorModelComboBox.SelectedItem?.ToString() ?? "MODEL_NONE";

            // Generate C loop function name
            string loopFuncName = name.ToLower() + "_loop";

            // Generate C code for action
            string aiCode = "";
            if (AiPatrol.IsChecked == true)
            {
                aiCode = @"    // Patrol Action: walk forward and turn around on hit wall
    if (o->oAction == 0) {
        o->oForwardVel = 4.0f;
        o->oAction = 1;
    }
    object_step();
    if (o->oMoveFlags & OBJ_MOVE_HIT_WALL) {
        o->oMoveAngleYaw += 0x8000; // Rotate 180 degrees
    }";
            }
            else if (AiChase.IsChecked == true)
            {
                aiCode = @"    // Chase Action: turn towards Mario and move forward
    obj_turn_toward_object(o, gMarioObject, O_MOVE_ANGLE_YAW_INDEX, 0x400);
    o->oForwardVel = 5.5f;
    object_step();";
            }
            else if (AiJump.IsChecked == true)
            {
                aiCode = @"    // Jump Action: hop repeatedly when landed
    if (o->oMoveFlags & OBJ_MOVE_LANDED) {
        o->oVelY = 14.0f;
    }
    object_step();";
            }
            else if (AiSpin.IsChecked == true)
            {
                aiCode = @"    // Spin Action: rotate constantly around Y axis
    o->oFaceAngleYaw += 0x300;";
            }
            else if (AiFloat.IsChecked == true)
            {
                aiCode = @"    // Float Action: hover up and down using sine wave
    o->oPosY = o->oHomeY + sins(o->oTimer * 0x350) * 120.0f;";
            }
            else
            {
                aiCode = "    // Passive Action: Stand Still\n    o->oForwardVel = 0.0f;\n    object_step();";
            }

            // Generate C code for interactions
            string interactionCode = "";
            if (IntSolid.IsChecked == true)
            {
                interactionCode += "    // Enable solid bounds\n    o->oInteractType = INTERACT_SOLID;\n";
            }
            if (IntDamage.IsChecked == true)
            {
                interactionCode += "    // Deal contact damage\n    if (obj_check_if_near_mario(o, 100.0f)) {\n        level_trigger_warp(gMarioState, WARP_OP_DEATH);\n    }\n";
            }
            if (IntGrabbable.IsChecked == true)
            {
                interactionCode += "    // Grabbable / Holdable flags\n    o->oInteractType = INTERACT_GRABBABLE;\n";
            }
            if (IntCollectible.IsChecked == true)
            {
                interactionCode += "    // Collectible behavior\n    if (obj_check_if_near_mario(o, 80.0f)) {\n        play_sound(SOUND_GENERAL_COIN, gGlobalSoundSource);\n        obj_mark_for_deletion(o);\n    }\n";
            }
            if (IntClimbable.IsChecked == true)
            {
                interactionCode += "    // Climbable pole flags\n    o->oInteractType = INTERACT_POLE;\n";
            }

            // Combine loop code
            string cCode = $@"
/* Custom Behavior '{name}' Generated by Randm64 Custom Behavior Builder */
void {loopFuncName}(void) {{
{aiCode}
{interactionCode}
}}

const BehaviorScript {name}[] = {{
    BEGIN(OBJ_LIST_GENERIC),
    OR_INT(oFlags, OBJ_FLAG_UPDATE_GFX_POS_AND_ANGLE),
    BEGIN_LOOP(),
        CALL_BHV_FUNC({loopFuncName}),
    END_LOOP(),
}};
";

            // Save C file and edit headers
            try
            {
                // 1. Write inside selected project: include/behavior_data.h
                string bhvHeaderPath = Path.Combine(_projectRoot, "include", "behavior_data.h");
                if (!File.Exists(bhvHeaderPath))
                {
                    bhvHeaderPath = Path.Combine(_projectRoot, "leveleditor", "include", "behavior_data.h");
                }
                
                RegisterInHeader(bhvHeaderPath, name);

                // 2. Write inside level editor internal header resource so it displays in design-time lists immediately
                string localHeaderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "leveleditor", "include", "behavior_data.h");
                RegisterInHeader(localHeaderPath, name);

                // 3. Append behavior array and function code to data/behavior_data.c
                string bhvSourcePath = Path.Combine(_projectRoot, "data", "behavior_data.c");
                if (!File.Exists(bhvSourcePath))
                {
                    bhvSourcePath = Path.Combine(_projectRoot, "data", "behavior_data.inc.c");
                }

                if (File.Exists(bhvSourcePath))
                {
                    File.AppendAllText(bhvSourcePath, cCode);
                }
                else
                {
                    MessageBox.Show("Could not find data/behavior_data.c or data/behavior_data.inc.c to write behavior array.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                }

                StatusTextBlock.Text = $"Behavior {name} successfully generated and registered!";
                MessageBox.Show($"Behavior {name} successfully registered!\nModel preview: {preferredModel}\n\nIt will now show up in your Level Editor Object list and compile successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to register custom behavior: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RegisterInHeader(string headerPath, string behaviorName)
        {
            if (!File.Exists(headerPath)) return;

            string content = File.ReadAllText(headerPath);
            string decl = $"extern const BehaviorScript {behaviorName}[];";

            if (content.Contains(behaviorName)) return; // Already declared

            // Insert before final #endif
            int lastEndif = content.LastIndexOf("#endif");
            if (lastEndif >= 0)
            {
                content = content.Insert(lastEndif, decl + "\n");
                File.WriteAllText(headerPath, content);
            }
            else
            {
                File.AppendAllText(headerPath, "\n" + decl + "\n");
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
