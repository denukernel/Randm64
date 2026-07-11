using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

namespace Sm64DecompLevelViewer.Services
{
    public class PluginService
    {
        private readonly SettingsService _settingsService;

        public PluginService()
        {
            _settingsService = new SettingsService();
        }

        public List<IPlugin> LoadPlugins(string projectRoot)
        {
            var loadedPlugins = new List<IPlugin>();
            string pluginsFolder = _settingsService.PluginsFolderPath;

            if (!Directory.Exists(pluginsFolder))
            {
                return loadedPlugins;
            }

            try
            {
                string[] dllFiles = Directory.GetFiles(pluginsFolder, "*.dll", SearchOption.TopDirectoryOnly);

                foreach (string dllFile in dllFiles)
                {
                    try
                    {
                        Assembly assembly = Assembly.LoadFrom(dllFile);
                        Type[] types = assembly.GetTypes();

                        foreach (Type type in types)
                        {
                            if (typeof(IPlugin).IsAssignableFrom(type) && !type.IsInterface && !type.IsAbstract)
                            {
                                if (Activator.CreateInstance(type) is IPlugin plugin)
                                {
                                    plugin.Initialize(projectRoot);
                                    loadedPlugins.Add(plugin);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error loading plugin assembly {dllFile}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scanning plugins folder: {ex.Message}");
            }

            return loadedPlugins;
        }
    }
}
