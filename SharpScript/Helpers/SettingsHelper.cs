﻿using MetroLog;
using MetroLog.Targets;
using SharpScript.ViewModels;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Windows.Storage;
using Windows.UI.Xaml;

namespace SharpScript.Helpers
{
    public static partial class SettingsHelper
    {
        public const string CachedCode = nameof(CachedCode);
        public const string OutputType = nameof(OutputType);
        public const string LanguageType = nameof(LanguageType);
        public const string SelectedAppTheme = nameof(SelectedAppTheme);
        public const string SelectedBackdrop = nameof(SelectedBackdrop);
        public const string IsExtendsTitleBar = nameof(IsExtendsTitleBar);

        public static Type Get<Type>(string key) => SystemTextJsonObjectSerializer.Deserialize<Type>(LocalObject.Values[key]?.ToString());
        public static void Set<Type>(string key, Type value) => LocalObject.Values[key] = SystemTextJsonObjectSerializer.Serialize(value);

        public static void SetDefaultSettings()
        {
            if (!LocalObject.Values.ContainsKey(CachedCode))
            {
                LocalObject.Values[CachedCode] = SystemTextJsonObjectSerializer.Serialize(
                    """
                    public class C {
                        public void M() {
                        }
                    }
                    """);
            }
            if (!LocalObject.Values.ContainsKey(OutputType))
            {
                LocalObject.Values[OutputType] = SystemTextJsonObjectSerializer.Serialize(ViewModels.OutputType.CSharp);
            }
            if (!LocalObject.Values.ContainsKey(LanguageType))
            {
                LocalObject.Values[LanguageType] = SystemTextJsonObjectSerializer.Serialize(ViewModels.LanguageType.CSharp);
            }
            if (!LocalObject.Values.ContainsKey(LanguageType))
            {
                LocalObject.Values[LanguageType] = SystemTextJsonObjectSerializer.Serialize(ViewModels.LanguageType.CSharp);
            }
            if (!LocalObject.Values.ContainsKey(SelectedAppTheme))
            {
                LocalObject.Values[SelectedAppTheme] = SystemTextJsonObjectSerializer.Serialize(ElementTheme.Default);
            }
            if (!LocalObject.Values.ContainsKey(IsExtendsTitleBar))
            {
                LocalObject.Values[IsExtendsTitleBar] = SystemTextJsonObjectSerializer.Serialize(true);
            }
        }
    }

    public static partial class SettingsHelper
    {
        public static ILogManager LogManager { get; } = LogManagerFactory.CreateLogManager(GetDefaultReleaseConfiguration());
        public static ApplicationDataContainer LocalObject { get; } = ApplicationData.Current.LocalSettings;

        static SettingsHelper() => SetDefaultSettings();

        private static LoggingConfiguration GetDefaultReleaseConfiguration()
        {
            string path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "MetroLogs");
            if (!Directory.Exists(path)) { Directory.CreateDirectory(path); }
            LoggingConfiguration loggingConfiguration = new();
            loggingConfiguration.AddTarget(LogLevel.Info, LogLevel.Fatal, new StreamingFileTarget(path, 7));
            return loggingConfiguration;
        }
    }

    public static class SystemTextJsonObjectSerializer
    {
        public static string Serialize<T>(T value) => value switch
        {
            bool => JsonSerializer.Serialize(value, SourceGenerationContext.Default.Boolean),
            string => JsonSerializer.Serialize(value, SourceGenerationContext.Default.String),
            OutputType => JsonSerializer.Serialize(value, SourceGenerationContext.Default.OutputType),
            ElementTheme => JsonSerializer.Serialize(value, SourceGenerationContext.Default.ElementTheme),
            LanguageType => JsonSerializer.Serialize(value, SourceGenerationContext.Default.LanguageType),
            _ => JsonSerializer.Serialize(value),
        };

        public static T Deserialize<T>([StringSyntax(StringSyntaxAttribute.Json)] string value)
        {
            if (string.IsNullOrEmpty(value)) { return default; }
            Type type = typeof(T);
            return type == typeof(bool) ? Deserialize(value, SourceGenerationContext.Default.Boolean)
                : type == typeof(string) ? Deserialize(value, SourceGenerationContext.Default.String)
                : type == typeof(OutputType) ? Deserialize(value, SourceGenerationContext.Default.OutputType)
                : type == typeof(ElementTheme) ? Deserialize(value, SourceGenerationContext.Default.ElementTheme)
                : type == typeof(LanguageType) ? Deserialize(value, SourceGenerationContext.Default.LanguageType)
                : JsonSerializer.Deserialize<T>(value);
            static T Deserialize<TValue>([StringSyntax(StringSyntaxAttribute.Json)] string json, JsonTypeInfo<TValue> jsonTypeInfo) => JsonSerializer.Deserialize(json, jsonTypeInfo) is T value ? value : default;
        }
    }

    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(OutputType))]
    [JsonSerializable(typeof(ElementTheme))]
    [JsonSerializable(typeof(LanguageType))]
    public partial class SourceGenerationContext : JsonSerializerContext;
}
