using System.Collections.ObjectModel;

public class MainViewModel
{
    public ObservableCollection<LangEntry> Entries { get; } = new();
}
