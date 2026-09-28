# Interface localization

CodexPetHub supports English (`en`) and French (`fr`). Open **Settings → Language**, choose a language and save. The whole window previews the choice immediately; Cancel reloads saved preferences. Pending edits survive page changes. The tray refreshes within one second after Save; the window stays open. Restarting is unnecessary.

The `language` field lives in the existing `%USERPROFILE%\.codex-pet-hub\config.json`, alongside brightness, audio, device pairing and sleep preferences. A configuration without this field uses the Windows installation UI language: French for `fr` cultures, English otherwise. An unsupported or null stored language falls back to English. Saving writes only the supported `en` or `fr` code using the existing atomic configuration store.

## Translation files

- [Strings.resx](../src/CodexPetHub.Core/Resources/Strings.resx): complete English source and fallback.
- [Strings.fr.resx](../src/CodexPetHub.Core/Resources/Strings.fr.resx): French satellite resources.
- [UiText.cs](../src/CodexPetHub.Core/UiText.cs): explicit culture, formatting and display-name helpers.

Use resource keys for all application-owned labels, menus, hints, validation messages and state names. Use numbered format arguments (`{0}`, `{1:F1}`) so translators can reorder phrases. Preserve placeholders exactly. Dates, decimals and booleans in diagnostics follow the selected language.

Keep protocol names, JSON fields, enum values, error codes, identifiers and log records stable. `UiMessage` carries a resource key and arguments alongside the English exception used in technical logs, allowing captured application errors to change language with the interface. Unexpected OS/driver exceptions appear under a translated “Technical details” label; their original message and firmware reset codes remain diagnostic data. Windows owns the stock file-dialog controls and may display those in its installed language.

## Adding another language

1. Add a complete `Strings.<culture>.resx` catalog.
2. Extend `UiText.Languages`, supported-language validation and the Settings page selector together. Update the system-language default if appropriate.
3. Add a localized language name to every catalog. Do not translate persisted codes.
4. Run `scripts/Build.ps1`, inspect previews, and check longer labels at the target Windows DPI.

The resource tests compare keys and placeholders. UI tests exercise Save/Cancel, edited field preservation, live menu/diagnostics updates, error translation and restart persistence. Generate off-screen previews with:

```powershell
dotnet run --project tests/CodexPetHub.UiTests -c Release -- --render artifacts/ui-previews
```

Distribute the entire publish directory, including `fr/CodexPetHub.Core.resources.dll`. Removing the satellite assembly causes .NET to fall back to English.
