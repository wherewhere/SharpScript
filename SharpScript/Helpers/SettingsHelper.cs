using CommunityToolkit.WinUI.Helpers;
using MetroLog;
using MetroLog.Targets;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI.ViewManagement;
using IObjectSerializer = CommunityToolkit.Common.Helpers.IObjectSerializer;

namespace SharpScript.Helpers
{
    public static partial class SettingsHelper
    {
        public const string CachedCode = nameof(CachedCode);
        public const string SelectedAppTheme = nameof(SelectedAppTheme);
        public const string SelectedBackdrop = nameof(SelectedBackdrop);
        public const string IsExtendsTitleBar = nameof(IsExtendsTitleBar);

        public static Type Get<Type>(string key) => LocalObject.Read<Type>(key);
        public static void Set<Type>(string key, Type value) => LocalObject.Save(key, value);
        public static Task<Type> GetFile<Type>(string key) => LocalObject.ReadFileAsync<Type>($"Settings/{key}");
        public static Task SetFile<Type>(string key, Type value) => LocalObject.CreateFileAsync($"Settings/{key}", value);

        public static void SetDefaultSettings()
        {
            if (!LocalObject.KeyExists(CachedCode))
            {
                LocalObject.Save(CachedCode, "1 + 1");
            }
            if (!LocalObject.KeyExists(SelectedAppTheme))
            {
                LocalObject.Save(SelectedAppTheme, ElementTheme.Default);
            }
            if (!LocalObject.KeyExists(IsExtendsTitleBar))
            {
                LocalObject.Save(IsExtendsTitleBar, true);
            }
        }
    }

    public static partial class SettingsHelper
    {
        public static UISettings UISettings { get; } = new();
        public static ILogManager LogManager { get; private set; }
        public static OSVersion OperatingSystemVersion => SystemInformation.Instance.OperatingSystemVersion;
        public static ApplicationDataStorageHelper LocalObject { get; } = ApplicationDataStorageHelper.GetCurrent(new SystemTextJsonObjectSerializer());

        static SettingsHelper() => SetDefaultSettings();

        public static void CreateLogManager()
        {
            if (LogManager == null)
            {
                string path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "MetroLogs");
                if (!Directory.Exists(path)) { Directory.CreateDirectory(path); }
                LoggingConfiguration loggingConfiguration = new();
                loggingConfiguration.AddTarget(LogLevel.Info, LogLevel.Fatal, new StreamingFileTarget(path, 7));
                LogManager = LogManagerFactory.CreateLogManager(loggingConfiguration);
            }
        }
    }

    public class SystemTextJsonObjectSerializer : IObjectSerializer
    {
        public string Serialize<T>(T value) => value switch
        {
            bool => JsonSerializer.Serialize(value, SourceGenerationContext.Default.Boolean),
            string => JsonSerializer.Serialize(value, SourceGenerationContext.Default.String),
            ElementTheme => JsonSerializer.Serialize(value, SourceGenerationContext.Default.ElementTheme),
#if DEBUG
            _ => JsonSerializer.Serialize(value)
#else
            _ => value?.ToString(),
#endif
        };

        public T Deserialize<T>(string value)
        {
            if (string.IsNullOrEmpty(value)) { return default; }
            Type type = typeof(T);
            return type == typeof(bool)
                ? Deserialize<T>(value, SourceGenerationContext.Default.Boolean)
                : type == typeof(string)
                    ? Deserialize<T>(value, SourceGenerationContext.Default.String)
                    : type == typeof(ElementTheme)
                        ? Deserialize<T>(value, SourceGenerationContext.Default.ElementTheme)
#if DEBUG
                        : JsonSerializer.Deserialize<T>(value);
#else
                        : default;
#endif
        }

        private T Deserialize<T>(string value, JsonTypeInfo context) =>
            JsonSerializer.Deserialize(value, context) is T result ? result : default;
    }

    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(ElementTheme))]
    public partial class SourceGenerationContext : JsonSerializerContext;
}
