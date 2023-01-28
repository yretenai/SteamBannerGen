using System;
using System.Globalization;
using System.IO;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
namespace SteamBannerGen;

// ReSharper disable AccessToDisposedClosure
internal class BannerGenerator {
    public static DirectoryInfo DefaultWindowsSteamPath { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));
    public static DirectoryInfo DefaultLinuxSteamPath { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".steam", "root"));
    public static DirectoryInfo DefaultMacSteamPath { get; } = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Steam"));


    private enum PinnedPosition {
        BottomLeft,
        UpperLeft,
        UpperCenter,
        CenterCenter,
        BottomCenter,
    }

    public DirectoryInfo SteamPath { get; }

    private const string LIBRARY_CACHE = "librarycache";
    private const string LIBRARY_CACHE_BACKUP = "librarycache_backup";
    private const string LIBRARY_CACHE_STAGING = "librarycache_staging";
    private const string IMAGE_TYPE_HEADER = "header";
    private const string IMAGE_TYPE_LOGO = "logo";
    private const string IMAGE_TYPE_HERO = "library_hero";
    private const double RATIO = 460 / 215d;

    public FileInfo GetLibraryFile(uint appId, string type, string imageType, string ext = "jpg") => new(Path.Combine(SteamPath.FullName, "appcache", type, $"{appId}_{imageType}.{ext}"));
    public DirectoryInfo GetLibraryDirectory(string type) => new(Path.Combine(SteamPath.FullName, "appcache", type));

    public BannerGenerator(string? steamPath) {
        if (steamPath is null) {
            if (OperatingSystem.IsWindows()) {
                SteamPath = DefaultWindowsSteamPath;
            } else if (OperatingSystem.IsLinux()) {
                SteamPath = DefaultLinuxSteamPath;
            } else if (OperatingSystem.IsMacOS()) {
                SteamPath = DefaultMacSteamPath;
            } else {
                throw new PlatformNotSupportedException();
            }
        } else {
            SteamPath = new DirectoryInfo(steamPath);
        }

        if (!SteamPath.Exists) {
            throw new DirectoryNotFoundException($"Steam path {SteamPath.FullName} does not exist. Please specify a valid path.");
        }

        Console.WriteLine($"Using Steam path {SteamPath.FullName}");


        var libraryCacheDirectory = GetLibraryDirectory(LIBRARY_CACHE_BACKUP);
        if (!libraryCacheDirectory.Exists) {
            libraryCacheDirectory.Create();
        }

        var libraryStagingDirectory = GetLibraryDirectory(LIBRARY_CACHE_STAGING);
        if (!libraryStagingDirectory.Exists) {
            libraryStagingDirectory.Create();
        }
    }

    public void GenerateBanners() {
        var appInfoPath = new FileInfo(Path.Combine(SteamPath.FullName, "appcache", "appinfo.vdf"));
        if (!appInfoPath.Exists) {
            throw new FileNotFoundException($"Appinfo file {appInfoPath.FullName} does not exist.");
        }

        Console.WriteLine($"Using appinfo file {appInfoPath.FullName}");

        var appInfo = new AppInfo(appInfoPath.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        foreach (var (appId, (_, data)) in appInfo.Entries) {
            var name = data["common"]?["type"];
            if (name?.ToString(CultureInfo.InvariantCulture).Equals("game", StringComparison.OrdinalIgnoreCase) == false) {
                continue;
            }

            var logoPosition = data["common"]?["library_assets"]?["logo_position"];
            if (logoPosition == default) {
                continue;
            }

            try {
                var pinnedPosition = Enum.Parse<PinnedPosition>(logoPosition["pinned_position"].ToString(CultureInfo.InvariantCulture), true);
                var widthPct = double.Parse(logoPosition["width_pct"].ToString(CultureInfo.InvariantCulture)) / 100;
                var heightPct = double.Parse(logoPosition["height_pct"].ToString(CultureInfo.InvariantCulture)) / 100;

                var heroFileInfo = GetLibraryFile(appId, LIBRARY_CACHE, IMAGE_TYPE_HERO);
                var logoFileInfo = GetLibraryFile(appId, LIBRARY_CACHE, IMAGE_TYPE_LOGO, "png");
                var headerFileInfo = GetLibraryFile(appId, LIBRARY_CACHE, IMAGE_TYPE_HEADER);

                if (!heroFileInfo.Exists) {
                    // Console.WriteLine($"Hero image for app {appId} does not exist.");
                    continue;
                }

                if (!logoFileInfo.Exists) {
                    // Console.WriteLine($"Logo image for app {appId} does not exist.");
                    continue;
                }

                using var hero = Image.Load(heroFileInfo.FullName);
                using var logo = Image.Load(logoFileInfo.FullName);
                using var header = new Image<Rgb24>((int) (hero.Height * RATIO), hero.Height);

                Console.WriteLine($"Generating banner for app {appId}");

                var centerXOffset = (hero.Width - header.Width) / 2d;
                var safeHeaderWidth = header.Width * 0.9d;
                var logoPct = Math.Min(widthPct, heightPct);
                logoPct = Math.Clamp(logoPct, 0.33d, 0.5d);
                var logoAspectRatio = logo.Width / (double) logo.Height;
                var logoWidth = safeHeaderWidth * logoPct;
                var logoHeight = logoWidth / logoAspectRatio;

                var logoX = pinnedPosition switch {
                                PinnedPosition.BottomLeft   => header.Width * 0.05d,
                                PinnedPosition.UpperLeft    => header.Width * 0.05d,
                                PinnedPosition.UpperCenter  => (header.Width - logoWidth) / 2d,
                                PinnedPosition.CenterCenter => (header.Width - logoWidth) / 2d,
                                PinnedPosition.BottomCenter => (header.Width - logoWidth) / 2d,
                                _                           => throw new IndexOutOfRangeException("Invalid pinned position"),
                            };

                var logoY = pinnedPosition switch {
                                // PinnedPosition.BottomLeft => header.Height * 0.95d - logoHeight,
                                PinnedPosition.BottomLeft   => (header.Height - logoHeight) / 2d,
                                PinnedPosition.UpperLeft    => header.Height * 0.05d,
                                PinnedPosition.UpperCenter  => header.Height * 0.05d,
                                PinnedPosition.CenterCenter => (header.Height - logoHeight) / 2d,
                                PinnedPosition.BottomCenter => (header.Height - logoHeight) / 2d,
                                // PinnedPosition.BottomCenter => header.Height * 0.95d - logoHeight,
                                _                           => throw new IndexOutOfRangeException("Invalid pinned position"),
                            };

                logo.Mutate(x => x.Resize((int) logoWidth, (int) logoHeight));

                header.Mutate(x => {
                                  x.DrawImage(hero, new Point((int) -centerXOffset, 0), 1f);
                                  x.DrawImage(logo, new Point((int) logoX, (int) logoY), 1f);
                              });

                var backup = GetLibraryFile(appId, LIBRARY_CACHE_BACKUP, IMAGE_TYPE_HEADER);
                if (headerFileInfo.Exists && !backup.Exists) {
                    headerFileInfo.CopyTo(backup.FullName, true);
                }

                var staging = GetLibraryFile(appId, LIBRARY_CACHE_STAGING, IMAGE_TYPE_HEADER);
                header.Save(staging.FullName);
                staging.CopyTo(headerFileInfo.FullName, true);

                GC.KeepAlive(hero);
                GC.KeepAlive(logo);
            } catch (Exception e) {
                Console.WriteLine($"Failed to parse logo position for app {appId}: {e.Message}");
            }
        }
    }
}
