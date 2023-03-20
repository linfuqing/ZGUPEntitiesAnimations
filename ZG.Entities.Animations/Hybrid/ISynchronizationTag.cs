namespace Unity.Animation.Hybrid
{
    public interface ISynchronizationTag
    {
        StringHash  Type { get; }
        int         State { get; set; }
    }
}
