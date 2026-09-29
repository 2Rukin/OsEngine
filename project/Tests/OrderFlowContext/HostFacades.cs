// Portable host facades only. Calculation, parser and Cloud detector sources are linked unchanged.
// Side is the exact numeric contract from Entity/Position.cs. Localization is never used by these tests.
namespace OsEngine.Entity { public enum Side { None = 0, Buy = 1, Sell = 2 } }
namespace OsEngine.Language
{
    public static class OsLocalization
    {
        public enum OsLocalType { None, Ru, Eng }
        public static OsLocalType CurLocalization => OsLocalType.Eng;
    }
}
