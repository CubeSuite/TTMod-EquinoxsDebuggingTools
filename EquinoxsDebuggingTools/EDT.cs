using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using FluffyUnderware.DevTools.Extensions;
using HarmonyLib;
using RewiredConsts;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static RootMotion.FinalIK.InteractionObject;

namespace EquinoxsDebuggingTools
{
    [BepInPlugin(MyGUID, PluginName, VersionString)]
    public class EDT : BaseUnityPlugin
    {
        private const string MyGUID = "com.equinox.EquinoxsDebuggingTools";
        private const string PluginName = "EquinoxsDebuggingTools";
        private const string VersionString = "2.0.0";

        private static readonly Harmony Harmony = new Harmony(MyGUID);
        internal static ManualLogSource log = new ManualLogSource(PluginName);

        // Objects & Variables

        internal static EDT instance;
        internal static float sSinceLastPacedLog;
        internal static Dictionary<string, ConfigEntry<bool>> shouldLogStatuses = new Dictionary<string, ConfigEntry<bool>>();

        #region Config Entries

        public static ConfigEntry<bool> forceDebugLoggingOff;
        public static ConfigEntry<bool> developerMode;

        #endregion

        private void Awake() {
            instance = this;
            Logger.LogInfo($"Config has {Config.Count} entries");
            foreach(KeyValuePair<ConfigDefinition,ConfigEntryBase> entry in Config) {
                Logger.LogInfo("Checking entry");
                if(entry.Value is ConfigEntry<bool> boolEntry) {
                    string key = entry.Key.Section.Replace("Mods.", "");
                    shouldLogStatuses.Add(key, boolEntry);
                    Logger.LogInfo($"Added entry: {key}, {boolEntry.Value}");
                }
            }

            Logger.LogInfo($"PluginName: {PluginName}, VersionString: {VersionString} is loading...");
            Harmony.PatchAll();

            CreateEDTConfigEntries();

            Logger.LogInfo($"PluginName: {PluginName}, VersionString: {VersionString} is loaded.");
            log = Logger;
        }

        private void Update() {
            sSinceLastPacedLog += Time.deltaTime;
        }

        // Public Functions

        /// <summary>
        /// Logs the message argument if the player has enabled the relevant config entry.
        /// </summary>
        /// <param name="category">The category of debug messages that the message belongs to. See library readme for examples.</param>
        /// <param name="message">The message to log</param>
        public static void Log(string category, string message) {
            string modName = GetCallingDll();
            string callingFunction = GetCallingFunction();

            if (!ShouldLogMessage(modName, category)) return;
            WriteToLog(category, message, callingFunction);
        }

        /// <summary>
        /// Logs the message argument if the player has enabled the relevant config entry and enough time has passed since the last call to PacedLog().
        /// </summary>
        /// <param name="category">The category of debug messages that the message belongs to. See library readme for examples.</param>
        /// <param name="message">The message to log</param>
        /// <param name="delaySeconds">How many seconds must pass before logging again. Default = 1s</param>
        public static void PacedLog(string category, string message, float delaySeconds = 1f) {
            string modName = GetCallingDll();
            string callingFunction = GetCallingFunction();

            if (!ShouldLogMessage(modName, category)) return;
            if (sSinceLastPacedLog < delaySeconds) return;

            WriteToLog(category, message, callingFunction);
            sSinceLastPacedLog = 0;
        }

        /// <summary>
        /// Checks if the provided object is null and logs if it is null
        /// </summary>
        /// <param name="obj">The object to be checked</param>
        /// <param name="name">The name of the object to add to the log line</param>
        /// <param name="shouldLog">Whether an info message should be logged if the object is not null</param>
        /// <returns>true if not null</returns>
        public static bool NullCheck(object obj, string name, bool shouldLog = false) {
            if (obj == null) {
                log.LogWarning($"{name} is null");
                return false;
            }
            else {
                if (shouldLog) log.LogInfo($"{name} is not null");
                return true;
            }
        }

        /// <summary>
        /// Loops through all members of 'obj' and logs its type, name and value.
        /// </summary>
        /// <param name="obj">The object to print all values of.</param>
        /// <param name="name">The name of the object to print at the start of the function.</param>
        public static void DebugObject(object obj, string name) {
            if (!NullCheck(obj, name)) {
                log.LogError("Can't debug null object");
                return;
            }

            Dictionary<Type, string> basicTypeNames = new Dictionary<Type, string>
            {
                { typeof(bool), "bool" },
                { typeof(byte), "byte" },
                { typeof(sbyte), "sbyte" },
                { typeof(char), "char" },
                { typeof(short), "short" },
                { typeof(ushort), "ushort" },
                { typeof(int), "int" },
                { typeof(uint), "uint" },
                { typeof(long), "long" },
                { typeof(ulong), "ulong" },
                { typeof(float), "float" },
                { typeof(double), "double" },
                { typeof(decimal), "decimal" },
                { typeof(string), "string" }
            };

            Type objType = obj.GetType();
            FieldInfo[] fields = objType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            log.LogInfo($"Debugging {objType.Name} '{name}':");
            foreach (FieldInfo field in fields) {
                string value = field.GetValue(obj)?.ToString() ?? "null";
                string type = basicTypeNames.ContainsKey(field.FieldType) ? basicTypeNames[field.FieldType] : field.FieldType.ToString();

                if (type == "char") value = $"'{value}'";
                else if (type == "string") value = $"\"{value}\"";

                log.LogInfo($"\t{type} {field.Name} = {value}");
            }
        }

        // Private Functions

        private void CreateEDTConfigEntries() {
            forceDebugLoggingOff = Config.Bind<bool>("EDT", "Force Debug Logging Off", true, new ConfigDescription("When enabled, no debug messages from mods using EDT will be logged."));
            developerMode = Config.Bind<bool>("EDT", "Developer Mode", false, new ConfigDescription("When enabled, new config entries will default to true"));
        }

        private static bool ShouldLogMessage(string modName, string category) {
            string key = $"{modName}.{category}";
            if (!shouldLogStatuses.ContainsKey(key)) {
                ConfigEntry<bool> newEntry = instance.Config.Bind(
                    $"Mods.{modName}", 
                    $"Debug {category}", 
                    developerMode.Value, 
                    new ConfigDescription($"Whether debug messages should be logged for {modName} - {category}")
                );

                shouldLogStatuses.Add(key, newEntry);
            }

            if (forceDebugLoggingOff.Value) return false;
            return shouldLogStatuses[key].Value;
        }

        private static void WriteToLog(string category, string message, string callingFunction) {
            string fullMessage = $"[{category}|{callingFunction}]: {message}";
            log.LogInfo(fullMessage);
        }

        private static string GetCallingFunction() {
            StackTrace stackTrace = new StackTrace();
            StackFrame frame = stackTrace.GetFrame(2);
            MethodBase method = frame.GetMethod();
            return method.DeclaringType.FullName + "." + method.Name;
        }

        private static string GetCallingDll() {
            StackTrace stackTrace = new StackTrace();
            StackFrame frame = stackTrace.GetFrame(2);
            MethodBase method = frame.GetMethod();
            Type declaringType = method.DeclaringType;
            Assembly assembly = declaringType.Assembly;
            return assembly.GetName().Name;
        }

        private string ProcessListOrArray(object input) {
            if (input == null) return "null";

            var array = (Array)input;
            var values = array.Cast<object>().Select(x => x.ToString());
            return string.Join(" ", values);
        }
    }
}
