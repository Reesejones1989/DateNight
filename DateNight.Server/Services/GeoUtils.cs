namespace DateNight.Server.Services;

public static class GeoUtils
{
    private const string Base32 = "0123456789bcdefghjkmnpqrstuvwxyz";

    /// <summary>Encodes a lat/lon as a geohash (Ticketmaster's geoPoint parameter).</summary>
    public static string Geohash(double lat, double lon, int precision = 9)
    {
        double latMin = -90, latMax = 90, lonMin = -180, lonMax = 180;
        var hash = new System.Text.StringBuilder();
        bool evenBit = true;
        int bit = 0, ch = 0;

        while (hash.Length < precision)
        {
            if (evenBit)
            {
                double mid = (lonMin + lonMax) / 2;
                if (lon >= mid) { ch = (ch << 1) | 1; lonMin = mid; } else { ch <<= 1; lonMax = mid; }
            }
            else
            {
                double mid = (latMin + latMax) / 2;
                if (lat >= mid) { ch = (ch << 1) | 1; latMin = mid; } else { ch <<= 1; latMax = mid; }
            }
            evenBit = !evenBit;
            if (++bit == 5) { hash.Append(Base32[ch]); bit = 0; ch = 0; }
        }
        return hash.ToString();
    }

    /// <summary>Great-circle distance in miles.</summary>
    public static double DistanceMiles(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 3958.8;
        double dLat = ToRad(lat2 - lat1), dLon = ToRad(lon2 - lon1);
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double ToRad(double deg) => deg * Math.PI / 180;
}
