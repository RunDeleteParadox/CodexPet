// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Globalization;
using System.Resources;

namespace CodexPetHub.Core;

// An explicit culture keeps UI choices independent of protocol and journal formats.
public sealed class UiText
{
    public static IReadOnlyList<string> Languages { get; } = Array.AsReadOnly(new[] { "en", "fr" });
    public static string DefaultLanguage => FromSystemCulture(CultureInfo.InstalledUICulture);
    public static string FromSystemCulture(CultureInfo culture) => culture.TwoLetterISOLanguageName == "fr" ? "fr" : "en";
    public static bool IsSupported(string? language) => language is "en" or "fr";
    public static string Normalize(string? language) => IsSupported(language) ? language! : "en";
    public static ResourceManager Resources { get; } = new("CodexPetHub.Core.Resources.Strings", typeof(UiText).Assembly);
    public string Language { get; }
    public CultureInfo Culture { get; }

    public UiText(string? language)
    {
        Language = Normalize(language);
        Culture = CultureInfo.GetCultureInfo(Language);
    }

    public string this[string key] => Resources.GetString(key, Culture) ?? throw new MissingManifestResourceException("Missing UI string: " + key);
    public string Format(string key, params object?[] args) => string.Format(Culture, this[key], args);
    public string State(BaseState state) => this["State" + state];
    public string Reaction(ReactionKind reaction) => this["Reaction" + reaction];
    public string Expression(string? expression) => expression == null ? this["Unknown"] :
        Resources.GetString("Expression_" + expression, Culture) ?? expression;
    public string Sound(SuccessSound sound) => this["Sound" + sound];
    public string Sound(string? sound) => sound is "none" or "fanfare" or "voice"
        ? Sound(Enum.Parse<SuccessSound>(sound, true)) : this["Unavailable"];
    public string Boolean(bool? value) => this[value is true ? "Yes" : value is false ? "No" : "Unavailable"];
    public string Health(string value) => Resources.GetString("Health_" + value, Culture) ?? value;
    public string Date(DateTimeOffset? value) => value?.ToLocalTime().ToString("G", Culture) ?? this["NotRecorded"];
    public string Number(long? value) => value?.ToString(Culture) ?? this["NotRecorded"];
    public string Power(string? value) => value == null ? this["Unavailable"] : Resources.GetString("Power_" + value, Culture) ?? value;
}

// Preserve the English technical exception and its type for logs/callers, while
// carrying a message that can be translated again when the user changes language.
public sealed record UiMessage(string Key, params object?[] Arguments)
{
    private const string DataKey = "CodexPetHub.UiMessage";
    public string Render(UiText text) => text.Format(Key, Arguments.Select(value => value is UiMessage message ? message.Render(text) : value).ToArray());
    public static UiMessage From(Exception error) => error.Data[DataKey] as UiMessage ?? new("TechnicalDetails", error.Message);
    public T Attach<T>(T error) where T : Exception { error.Data[DataKey] = this; return error; }
    public static InvalidDataException Invalid(string key, params object?[] args)
    {
        var message = new UiMessage(key, args);
        return message.Attach(new InvalidDataException(message.Render(new("en"))));
    }
    public static IOException Io(string key, params object?[] args)
    {
        var message = new UiMessage(key, args);
        return message.Attach(new IOException(message.Render(new("en"))));
    }
}
