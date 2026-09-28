// SPDX-FileCopyrightText: 2026 RunDeleteParadox and contributors
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.IO;
using System.Text.Json;
class Program {
    static void Main() {
        var root = AppDomain.CurrentDomain.BaseDirectory;
        string line;
        while ((line = Console.ReadLine()) != null) {
            using var doc = JsonDocument.Parse(line);
            var request = doc.RootElement;
            if (!request.TryGetProperty("id", out var id)) continue;
            var method = request.GetProperty("method").GetString();
            object result;
            if (method == "initialize") result = new { userAgent = "offline-test" };
            else if (method == "thread/turns/list") {
                var args = request.GetProperty("params");
                if (args.GetProperty("itemsView").GetString() != "notLoaded" || args.GetProperty("limit").GetInt32() != 1)
                    throw new Exception("The client requested message content.");
                File.AppendAllText(Path.Combine(root, "requests.txt"), method + " notLoaded limit=1\n");
                result = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(root, "page.json")));
            } else throw new Exception("Unexpected mutating method: " + method);
            Console.WriteLine(JsonSerializer.Serialize(new { id = id.GetInt32(), result }));
            Console.Out.Flush();
        }
    }
}
