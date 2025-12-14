using System.Collections.Generic;

public class LangEntry
{
    public string Key { get; set; }

    // languageCode -> text
    public Dictionary<string, string> Values { get; set; } = new();
}
