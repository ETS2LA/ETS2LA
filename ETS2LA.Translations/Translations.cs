using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.FileProviders;
using OrchardCore.Localization;
using GTranslate.Translators;

namespace ETS2LA.Translations;

public class Language
{
    public string EnglishName { get; set; } = string.Empty;
    public string NativeName { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}

public static class Languages
{    
    public static List<Language> SupportedLanguages = new List<Language>
    {
        new Language { EnglishName = "Finnish", NativeName = "Suomi", Code = "fi" },
        new Language { EnglishName = "English", NativeName = "English", Code = "en" },
        new Language { EnglishName = "Chinese (Simplified)", NativeName = "简体中文", Code = "zh_Hans" },
        new Language { EnglishName = "Chinese (Traditional)", NativeName = "繁體中文", Code = "zh_Hant" },
        new Language { EnglishName = "Estonian", NativeName = "Eesti", Code = "et" },
        new Language { EnglishName = "French", NativeName = "Français", Code = "fr" },
        new Language { EnglishName = "German", NativeName = "Deutsch", Code = "de" },
        new Language { EnglishName = "Hindi (Hinglish)", NativeName = "हिंदी", Code = "hi@hinglish" },
        new Language { EnglishName = "Hungarian", NativeName = "Magyar", Code = "hu" },
        new Language { EnglishName = "Japanese", NativeName = "日本語", Code = "ja" },
        new Language { EnglishName = "Korean", NativeName = "한국어", Code = "ko" },
        new Language { EnglishName = "Polish", NativeName = "Polski", Code = "pl" },
        new Language { EnglishName = "Russian", NativeName = "Русский", Code = "ru" },
        new Language { EnglishName = "Slovak", NativeName = "Slovenčina", Code = "sk" },
        new Language { EnglishName = "Spanish (American)", NativeName = "Español (Estados Unidos)", Code = "es_US" },
        new Language { EnglishName = "Turkish", NativeName = "Türkçe", Code = "tr" },
        new Language { EnglishName = "Vietnamese", NativeName = "Tiếng Việt", Code = "vi" },
    }.OrderBy(l => l.EnglishName).ToList();
}


public static class T
{
    private static IStringLocalizer? _localizer;
    private static IServiceProvider? _serviceProvider;

    public static void Initialize(IServiceProvider provider)
    {
        var factory = provider.GetRequiredService<IStringLocalizerFactory>();
        _localizer = factory.Create(typeof(T));
        _serviceProvider = provider;
    }

    /// <summary>
    ///  Translates a string.
    ///  `_("My String {0}", var);`
    /// </summary>
    public static string _(string name, params object[] arguments)
    {
        if (_localizer == null) return string.Format(name, arguments);
        return _localizer[name, arguments];
    }


    /// <summary>
    ///  Translates a string with pluralization.
    ///  ```
    ///  _n("Singular {0} value", "Plural {0} values", count, var);
    ///  ```
    /// </summary>
    public static string _n(string singular, string plural, int count, params object[] arguments)
    {
        if (_localizer == null) 
            return string.Format(count == 1 ? singular : plural, arguments);

        return _localizer.Plural(count, singular, plural, arguments.Skip(1).ToArray());
    }

    /// <summary>
    ///  Gets the credits for a specific language code.
    /// </summary>
    public static List<string> LanguageCredits(string languageCode)
    {
        // Credits are stored in the first comment lines of all translation files. For example:
        // # Tumppi066 <tumppi066@ets2la.com>, 2026, 2025.
        // # Dylan <dylan@ets2la.com>, 2026.
        var assembly = typeof(T).Assembly;
        var resourceName = $"ETS2LA.Translations.Localization.{languageCode}.po";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null) return new List<string> { "No credits available." };
        using var reader = new StreamReader(stream);
        var credits = new List<string>();
        while (!reader.EndOfStream)
        {
            var line = reader.ReadLine();
            if (line == null) break;
            if (line.StartsWith("#"))
            {
                var credit = line.Substring(1).Trim();
                if (!string.IsNullOrEmpty(credit))
                {
                    var emailIndex = credit.IndexOf('<') - 1; // Removing the space before the email
                    var emailEndIndex = credit.IndexOf('>');
                    if (emailIndex >= 0 && emailEndIndex > emailIndex)
                        credit = credit.Remove(emailIndex, emailEndIndex - emailIndex + 1).Trim();
                    
                    credits.Add(credit);
                }
            }
            else
            {
                break; // Stop reading after the first non-comment line
            }
        }
        return credits;
    }
}


public class DynamicTranslationTask
{
    private AggregateTranslator translator = new AggregateTranslator(new ITranslator[]
    {
        new GoogleTranslator(),
        new GoogleTranslator2(),
        new BingTranslator(),
        new YandexTranslator()
    });

    public event Action<string>? OnTranslationCompleted;
    public string SourceText { get; private set; } = string.Empty;
    public string TargetLanguage { get; private set; } = string.Empty;

    private string result = "";

    public DynamicTranslationTask(string sourceText, string targetLanguage)
    {
        SourceText = sourceText;
        TargetLanguage = targetLanguage;
    }

    public async Task StartTranslationAsync()
    {
        try
        {
            var translatedText = await translator.TranslateAsync(SourceText, TargetLanguage);
            result = translatedText.Translation;
            OnTranslationCompleted?.Invoke(translatedText.Translation);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Dynamic translation failed: {ex.Message}");
            result = SourceText;
            OnTranslationCompleted?.Invoke(SourceText);
        }
    }

    public string Translate()
    {
        Task.Run(async () => await StartTranslationAsync());
        return result;
    }
}