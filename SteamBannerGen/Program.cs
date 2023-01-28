using System.Buffers.Binary;
using System.Linq;

namespace SteamBannerGen;

internal static class Program {
    public static void Main(string[] args) {
        new BannerGenerator(args.ElementAtOrDefault(0)).GenerateBanners();
    }
}
