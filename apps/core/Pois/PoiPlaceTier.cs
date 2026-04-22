namespace VinhKhanh.Core.Pois;

public enum PoiPlaceTier
{
    Basic = 0,
    Premium = 1
}

public static class PoiPlaceTierCatalog
{
    public const PoiPlaceTier Default = PoiPlaceTier.Basic;

    public static PoiPlaceTier Normalize(PoiPlaceTier value)
        => value is PoiPlaceTier.Basic or PoiPlaceTier.Premium
            ? value
            : Default;

    public static PoiPlaceTier FromInt(int value)
        => value == (int)PoiPlaceTier.Premium
            ? PoiPlaceTier.Premium
            : PoiPlaceTier.Basic;
}
