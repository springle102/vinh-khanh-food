using VinhKhanh.BackendApi.Infrastructure;

var failures = new List<string>();

void AssertEqual(string name, string actual, string expected)
{
    if (!string.Equals(actual, expected, StringComparison.Ordinal))
    {
        failures.Add($"{name}: expected '{expected}' but got '{actual}'");
    }
}

var reverseNormalized = PoiAddressNormalizer.NormalizeGeocodingAddress(new PoiAddressParts(
    DisplayName: "Ốc Phát, Vĩnh Khánh, Phường Khánh Hội, Thành phố Thủ Đức, Thành phố Hồ Chí Minh, 72806, Việt Nam",
    Venue: "Ốc Phát",
    Road: "Vĩnh Khánh",
    Suburb: "Phường Khánh Hội",
    City: "Thành phố Thủ Đức",
    Country: "Việt Nam",
    CountryCode: "vn",
    Iso3166Level4: "VN-SG",
    Lat: 10.761873,
    Lng: 106.702153));

AssertEqual("reverse.address", reverseNormalized.Address, "Ốc Phát, Vĩnh Khánh, Phường Khánh Hội, Quận 4, TP.HCM, Việt Nam");
AssertEqual("reverse.district", reverseNormalized.District, "Quận 4");
AssertEqual("reverse.ward", reverseNormalized.Ward, "Phường Khánh Hội");

var storedBadPoi = PoiAddressNormalizer.NormalizeStoredPoiAddress(
    "Nhà Hàng Sushi Ko, 122/37/15 Vĩnh Khánh, Phường Khánh Hội, Thành phố Thủ Đức, Việt Nam",
    "Thành phố Thủ Đức",
    "Phường Khánh Hội",
    10.760772,
    106.704798);

AssertEqual("stored.address", storedBadPoi.Address, "Nhà Hàng Sushi Ko, 122/37/15 Vĩnh Khánh, Phường Khánh Hội, Quận 4, TP.HCM, Việt Nam");
AssertEqual("stored.district", storedBadPoi.District, "Quận 4");
AssertEqual("stored.ward", storedBadPoi.Ward, "Phường Khánh Hội");

var validThuDucPoi = PoiAddressNormalizer.NormalizeStoredPoiAddress(
    "Cafe Riverside, Nguyễn Duy Trinh, Phường An Khánh, Thành phố Thủ Đức, TP.HCM, Việt Nam",
    "Thành phố Thủ Đức",
    "Phường An Khánh",
    10.803200,
    106.744000);

AssertEqual("thu-duc.address", validThuDucPoi.Address, "Cafe Riverside, Nguyễn Duy Trinh, Phường An Khánh, Thành phố Thủ Đức, TP.HCM, Việt Nam");
AssertEqual("thu-duc.district", validThuDucPoi.District, "Thành phố Thủ Đức");
AssertEqual("thu-duc.ward", validThuDucPoi.Ward, "Phường An Khánh");

if (failures.Count > 0)
{
    Console.Error.WriteLine("PoiAddressNormalizationSmoke failed:");
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }

    Environment.Exit(1);
}

Console.WriteLine("PoiAddressNormalizationSmoke passed.");
