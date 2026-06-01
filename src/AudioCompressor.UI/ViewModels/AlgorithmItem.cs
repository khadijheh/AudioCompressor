namespace AudioCompressor.UI.ViewModels;

public record DisplayItem<T>(string Name, T Value)
{
    public override string ToString() => Name;
}
