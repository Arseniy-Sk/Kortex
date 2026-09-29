using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kortex.Models;

namespace Kortex.Services
{
    /// <summary>Хранение профилей в %AppData%\KeyboardStudio\profiles.json.</summary>
    public static class ProfileStore
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static string Folder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Kortex");

        public static string FilePath => Path.Combine(Folder, "profiles.json");

        public static AppState Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var json = File.ReadAllText(FilePath);
                    var state = JsonSerializer.Deserialize<AppState>(json, Options);
                    if (state != null)
                    {
                        // Профиль без единого макроса получает заготовки — своё ничего не трогаем
                        if (state.Active.Macros.Count == 0) MacroPresets.EnsureIn(state.Active);
                        return state;
                    }
                }
            }
            catch { /* повреждённый файл — стартуем с чистого состояния */ }

            // Первый запуск: кладём в профиль готовые макросы, чтобы было с чем работать сразу
            var fresh = new AppState();
            MacroPresets.EnsureIn(fresh.Active);
            return fresh;
        }

        public static void Save(AppState state)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(state, Options));
            }
            catch { /* нет прав на запись — молча продолжаем работу в памяти */ }
        }

        public static void Export(Profile profile, string path) =>
            File.WriteAllText(path, JsonSerializer.Serialize(profile, Options));

        public static Profile Import(string path) =>
            JsonSerializer.Deserialize<Profile>(File.ReadAllText(path), Options);
    }
}
