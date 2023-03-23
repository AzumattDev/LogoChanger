using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.IO.Compression;

namespace LogoChanger
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class LogoChangerPlugin : BaseUnityPlugin
    {
        internal const string ModName = "LogoChanger";
        internal const string ModVersion = "1.0.5";
        internal const string Author = "Azumatt";
        private const string ModGUID = Author + "." + ModName;
        private static readonly string ConfigFileName = ModGUID + ".cfg";

        private static readonly string ConfigFileFullPath =
            Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;

        private readonly Harmony _harmony = new(ModGUID);

        private static readonly ManualLogSource LogoChangerLogger =
            BepInEx.Logging.Logger.CreateLogSource(ModName);

        private enum Toggle
        {
            On = 1,
            Off = 0
        }

        public void Awake()
        {
            _modEnabled = Config.Bind("1 - General", "Mod Enabled?", Toggle.On, "Enable/Disable the mod");
            _mainMenuLogo = Config.Bind("2 - Main Menu", "Main Menu Logo", "LogoChanger_LOGO.png",
                "The logo to use on the main menu to replace \"Valheim\" image, should be found somewhere in the plugins folder and sized at 1000x394");
            _mistMenuLogo = Config.Bind("2 - Main Menu", "Mislands Menu Logo", "LogoChanger_MistlandsLogo.png",
                "The logo to use on the main menu to replace \"Valheim\" image, should be found somewhere in the plugins folder and sized at 2048x448");
            _modEnabled.SettingChanged += ReloadImagesFromFolder;

            MoveImagesToConfigFolder();
            ReloadImagesFromFolder(null!, null!);
            Assembly assembly = Assembly.GetExecutingAssembly();
            _harmony.PatchAll(assembly);
            SetupWatcher();
        }

        private void OnDestroy()
        {
            Config.Save();
        }

        private void SetupWatcher()
        {
            FileSystemWatcher configWatcher = new(Paths.ConfigPath, ConfigFileName);
            configWatcher.Changed += ReadConfigValues;
            configWatcher.Created += ReadConfigValues;
            configWatcher.Renamed += ReadConfigValues;
            configWatcher.IncludeSubdirectories = true;
            configWatcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            configWatcher.EnableRaisingEvents = true;

            FileSystemWatcher imageWatcher = new(Paths.ConfigPath, "LogoChanger*.png");
            imageWatcher.Changed += ReloadImagesFromFolder;
            imageWatcher.Created += ReloadImagesFromFolder;
            imageWatcher.Renamed += ReloadImagesFromFolder;
            imageWatcher.IncludeSubdirectories = true;
            imageWatcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
            imageWatcher.EnableRaisingEvents = true;
        }

        private static void MoveImagesToConfigFolder()
        {
            string destinationFolderPath = Path.Combine(Paths.ConfigPath, "Azumatt.LogoChanger_Images");
            if (!Directory.Exists(destinationFolderPath))
            {
                Directory.CreateDirectory(destinationFolderPath);
            }

            string[] files = Directory.GetFiles(Paths.PluginPath, "LogoChanger*.png", SearchOption.AllDirectories);
            string[] zipFiles = Directory.GetFiles(Paths.PluginPath, "DefaultLogos.zip", SearchOption.AllDirectories);

            if (zipFiles.Length > 0)
            {
                string sourceZipFile = zipFiles[0];
                string destinationZipFile = Path.Combine(destinationFolderPath, "DefaultLogos.zip");
                if (File.Exists(destinationZipFile))
                {
                    File.Delete(destinationZipFile);
                }

                File.Move(sourceZipFile, destinationZipFile);
            }

            foreach (string file in files)
            {
                string fileName = Path.GetFileName(file);
                string destinationFilePath = Path.Combine(destinationFolderPath, fileName);
                File.Move(file, destinationFilePath);
            }
        }

        private void ReadConfigValues(object sender, FileSystemEventArgs e)
        {
            if (!File.Exists(ConfigFileFullPath)) return;
            try
            {
                LogoChangerLogger.LogDebug("ReadConfigValues called");
                Config.Reload();
            }
            catch
            {
                LogoChangerLogger.LogError($"There was an issue loading your {ConfigFileName}");
                LogoChangerLogger.LogError("Please check your config entries for spelling and format!");
            }
        }

        internal static void ReloadImagesFromFolder(object sender, FileSystemEventArgs e)
        {
            Load("(file system)");
            if (CheckIfStartScene() && _modEnabled.Value == Toggle.On)
                FindMenuLogos();
        }

        private static void ReloadImagesFromFolder(object sender, EventArgs e)
        {
            Load("(setting changed)");
            if (CheckIfStartScene() && _modEnabled.Value == Toggle.On)
                FindMenuLogos();
        }

        private static void Load(string calledMethod)
        {
            LogoChangerLogger.LogDebug($"ReloadImagesFromFolder {calledMethod} called");
            if (Player.m_localPlayer != null)
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Reloading images...please wait...", 12);


            if (_modEnabled.Value != Toggle.On) return;
            _mainLogoSprite = LoadSprite(_mainMenuLogo.Value);
            _mistLogoSprite = LoadSprite(_mistMenuLogo.Value);
        }

        private static Sprite LoadSprite(string name, bool isEmbed = false)
        {
            Texture2D texture = LoadTexture(name, isEmbed);
            return texture != null
                ? Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.zero)
                : null!;
        }

        private static Texture2D LoadTexture(string name, bool isEmbed)
        {
            Texture2D texture = new(0, 0);
            string? directoryName = Path.GetDirectoryName(Paths.ConfigPath);
            if (directoryName == null) return texture;
            List<string> paths = Directory.GetFiles(directoryName, "LogoChanger*.png", SearchOption.AllDirectories)
                .OrderBy(Path.GetFileName).ToList();
            try
            {
                byte[] fileData = File.ReadAllBytes(paths.Find(x => x.Contains(name)));
                texture.LoadImage(fileData);
            }
            catch (Exception e)
            {
                LogoChangerLogger.LogWarning(
                    $"The file {name} couldn't be found in the directory path. Please make sure you are naming your files correctly and they are location somewhere in the BepInEx/plugins folder.\n" +
                    $" Optionally, you can turn off 'Use Custom Backgrounds' inside of your configuration file. If you no longer wish to see this error.\n {e}");
                texture = null!;
            }


            return texture!;
        }

        private static bool CheckIfStartScene()
        {
            return SceneManager.GetActiveScene().name == "start";
        }

        private static void FindMenuLogos()
        {
            if(_mainLogoSprite == null || _mistLogoSprite == null) return;
            Transform? logoTransform = Utils.FindChild(FejdStartup.m_instance.m_mainMenu.transform, "Logo")?.transform;
            try
            {
                Utils.FindChild(logoTransform, "LOGO").gameObject.GetComponent<Image>().sprite = _mainLogoSprite;
            }
            catch (Exception e)
            {
                LogoChangerLogger.LogError(
                    $"Couldn't find LOGO in hierarchy of the main menu or couldn't assign the LOGO sprite. {e}");
                throw;
            }

            try
            {
                Utils.FindChild(logoTransform, "MistlandsLogo").GetComponent<Image>().sprite = _mistLogoSprite;
            }
            catch (Exception e)
            {
                LogoChangerLogger.LogError(
                    $"Couldn't find MistlandsLogo in hierarchy of the main menu or couldn't assign the MistlandsLogo sprite. {e}");
                throw;
            }

            try
            {
                Utils.FindChild(logoTransform, "Mist (1)").gameObject.SetActive(false);
            }
            catch (Exception e)
            {
                LogoChangerLogger.LogError(
                    $"Couldn't find Mist (1) in hierarchy of the main menu or couldn't disable Mist (1) gameobject. {e}");
                throw;
            }

            try
            {
                Utils.FindChild(logoTransform, "Mist (2)").gameObject.SetActive(false);
            }
            catch (Exception e)
            {
                LogoChangerLogger.LogError(
                    $"Couldn't find Mist (2) in hierarchy of the main menu or couldn't disable Mist (2) gameobject. {e}");
                throw;
            }

            try
            {
                Utils.FindChild(logoTransform, "Mist (3)").gameObject.SetActive(false);
            }
            catch (Exception e)
            {
                LogoChangerLogger.LogError(
                    $"Couldn't find Mist (3) in hierarchy of the main menu or couldn't disable Mist (3) gameobject. {e}");
                throw;
            }

            try
            {
                Utils.FindChild(logoTransform, "Embers (1)").gameObject.SetActive(false);
            }
            catch (Exception e)
            {
                LogoChangerLogger.LogError(
                    $"Couldn't find Embers (1) in hierarchy of the main menu or couldn't disable Embers (1) gameobject. {e}");
                throw;
            }

            try
            {
                Utils.FindChild(logoTransform, "Embers (2)").gameObject.SetActive(false);
            }
            catch (Exception e)
            {
                LogoChangerLogger.LogError(
                    $"Couldn't find Embers (2) in hierarchy of the main menu or couldn't disable Embers (2) gameobject. {e}");
                throw;
            }
        }


        #region ConfigOptions

        private static ConfigEntry<Toggle> _modEnabled = null!;
        private static ConfigEntry<string> _mainMenuLogo = null!;
        private static ConfigEntry<string> _mistMenuLogo = null!;
        private static Sprite _mainLogoSprite = null!;
        private static Sprite _mistLogoSprite = null!;

        #endregion
    }
    
    [HarmonyPatch(typeof(Player),nameof(Player.Awake))]
    static class PlayerAwakePatch
    {
        static void Prefix(Player __instance)
        {
            LogoChangerPlugin.ReloadImagesFromFolder(null!, null!);
        }
    }
}