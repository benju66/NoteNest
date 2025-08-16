namespace NoteNest.Core.Interfaces.Split
{
    public interface ISplitElement
    {
        string Id { get; }
        bool IsActive { get; set; }
        double MinWidth { get; }
        double MinHeight { get; }
    }

    public enum SplitOrientation
    {
        Horizontal,
        Vertical
    }
}


