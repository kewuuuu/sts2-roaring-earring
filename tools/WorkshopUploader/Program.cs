using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Steamworks;

namespace WorkshopUploader;

/// <summary>
/// Publishes dist/workshop to the Steam Workshop (appid 2868840). Publishing is opt-in:
/// only --create and --update touch the Workshop; --dry-run validates and prints, nothing else.
/// </summary>
internal static class Program
{
    private const uint GameAppId = 2868840;
    private const string ModId = "sts2-roaring-earring";
    private const long MinModDllBytes = 60000;
    private const int MaxTitleChars = 128;
    private const int MaxDescriptionChars = 8000;
    private const long MaxPreviewBytes = 1024 * 1024;
    private const int FileListLimit = 10;
    private const int CreateTimeoutMs = 60000;
    private const int SubmitTimeoutMs = 600000;
    private const string FirstReleaseNote = "\u9996\u6b21\u53d1\u5e03";

    private enum Mode
    {
        DryRun,
        Probe,
        Create,
        Update,
    }

    private sealed class Options
    {
        public Mode Mode { get; set; } = Mode.DryRun;

        public ulong PublishedFileId { get; set; }

        public string Content { get; set; } = string.Empty;

        public string Preview { get; set; } = string.Empty;

        public string TitleFile { get; set; } = string.Empty;

        public string DescriptionFile { get; set; } = string.Empty;

        public string ChangelogFile { get; set; } = string.Empty;

        public string BranchMin { get; set; } = string.Empty;

        public string BranchMax { get; set; } = string.Empty;
    }

    private sealed class Reporter
    {
        public List<string> Problems { get; } = new();

        public void Missing(string message) => Problems.Add("MISSING " + message);

        public void Bad(string message) => Problems.Add("BAD " + message);
    }

    private sealed record ContentFile(string RelativePath, long Bytes);


    private static int Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
        }
        catch (IOException)
        {
            // A redirected stdout can refuse the encoding switch; the log still works.
        }

        if (!TryParseOptions(args, out Options options, out string parseError))
        {
            Log(parseError);
            return 1;
        }

        string repoRoot = FindRepoRoot();
        ApplyDefaults(options, repoRoot);
        Log("mode " + ModeName(options.Mode));

        var report = new Reporter();
        bool steamReady = false;

        if (EnsureSteamAppIdFile(report))
        {
            if (SteamAPI.Init())
            {
                steamReady = true;
                Log($"appid={SteamUtils.GetAppID().m_AppId} steamid={SteamUser.GetSteamID().m_SteamID} persona={SteamFriends.GetPersonaName()}");
            }
            else
            {
                report.Bad("SteamAPI.Init() returned false (is the Steam client running and logged in?)");
            }
        }

        try
        {
            if (options.Mode == Mode.Probe)
            {
                foreach (string problem in report.Problems)
                {
                    Log(problem);
                }

                if (!steamReady)
                {
                    Log("BAD Steam is not initialized");
                    return 1;
                }

                return RunProbe();
            }

            string? title = LoadText(options.TitleFile, "title", report);
            string? description = LoadText(options.DescriptionFile, "description", report);
            string? changelog;
            if (options.Mode == Mode.Create)
            {
                changelog = FirstReleaseNote;
                Log("changelog " + FirstReleaseNote + " (--create ignores the changelog file)");
            }
            else
            {
                changelog = LoadText(options.ChangelogFile, "changelog", report);
            }

            Log("visibility public");
            CheckTitle(title, report);
            CheckDescription(description, report);
            CheckPreview(options.Preview, report);
            CheckContent(options.Content, report);
            CheckVersionMentions(options.Content, description, report);
            CheckBranchVersions(options, report);

            foreach (string problem in report.Problems)
            {
                Log(problem);
            }

            if (report.Problems.Count > 0)
            {
                Log(options.Mode == Mode.DryRun ? "dry-run failed" : "validation failed, nothing submitted");
                return 1;
            }

            if (options.Mode == Mode.DryRun)
            {
                Log("dry-run ok");
                return 0;
            }

            if (!steamReady)
            {
                Log("BAD Steam is not initialized");
                return 1;
            }

            string publishTitle = title ?? string.Empty;
            string publishDescription = description ?? string.Empty;
            string publishNote = changelog ?? string.Empty;

            return options.Mode == Mode.Create
                ? RunCreate(options, repoRoot, publishTitle, publishDescription, publishNote)
                : RunUpdate(options, publishTitle, publishDescription, publishNote);
        }
        finally
        {
            if (steamReady)
            {
                SteamAPI.Shutdown();
            }
        }
    }

    private static bool TryParseOptions(string[] args, out Options options, out string error)
    {
        options = new Options();
        error = string.Empty;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "--dry-run":
                    options.Mode = Mode.DryRun;
                    break;
                case "--create":
                    options.Mode = Mode.Create;
                    break;
                case "--probe":
                    options.Mode = Mode.Probe;
                    break;
                case "--update":
                    if (!TakeValue(args, ref i, arg, out string idText, out error))
                    {
                        return false;
                    }

                    if (!ulong.TryParse(idText, NumberStyles.None, CultureInfo.InvariantCulture, out ulong publishedFileId) || publishedFileId == 0)
                    {
                        error = $"BAD --update expects a positive published file id, got '{idText}'";
                        return false;
                    }

                    options.PublishedFileId = publishedFileId;
                    options.Mode = Mode.Update;
                    break;
                case "--content":
                    if (!TakeValue(args, ref i, arg, out string content, out error))
                    {
                        return false;
                    }

                    options.Content = content;
                    break;
                case "--preview":
                    if (!TakeValue(args, ref i, arg, out string preview, out error))
                    {
                        return false;
                    }

                    options.Preview = preview;
                    break;
                case "--title-file":
                    if (!TakeValue(args, ref i, arg, out string titleFile, out error))
                    {
                        return false;
                    }

                    options.TitleFile = titleFile;
                    break;
                case "--description-file":
                    if (!TakeValue(args, ref i, arg, out string descriptionFile, out error))
                    {
                        return false;
                    }

                    options.DescriptionFile = descriptionFile;
                    break;
                case "--changelog-file":
                    if (!TakeValue(args, ref i, arg, out string changelogFile, out error))
                    {
                        return false;
                    }

                    options.ChangelogFile = changelogFile;
                    break;
                case "--branch-min":
                    if (!TakeValue(args, ref i, arg, out string branchMin, out error))
                    {
                        return false;
                    }

                    options.BranchMin = branchMin;
                    break;
                case "--branch-max":
                    if (!TakeValue(args, ref i, arg, out string branchMax, out error))
                    {
                        return false;
                    }

                    options.BranchMax = branchMax;
                    break;
                default:
                    error = $"BAD unknown argument '{arg}'";
                    return false;
            }
        }

        return true;
    }

    private static bool TakeValue(string[] args, ref int index, string name, out string value, out string error)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            error = $"BAD {name} expects a value";
            return false;
        }

        value = args[++index];
        error = string.Empty;
        return true;
    }

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "sts2-roaring-earring.csproj")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return Environment.CurrentDirectory;
    }

    private static void ApplyDefaults(Options options, string repoRoot)
    {
        if (options.Content.Length == 0)
        {
            options.Content = Path.Combine(repoRoot, "dist", "workshop");
        }

        if (options.Preview.Length == 0)
        {
            options.Preview = Path.Combine(repoRoot, "assets", "icons", "roaring_earring_big.png");
        }

        if (options.TitleFile.Length == 0)
        {
            options.TitleFile = Path.Combine(repoRoot, "workshop", "title.txt");
        }

        if (options.DescriptionFile.Length == 0)
        {
            options.DescriptionFile = Path.Combine(repoRoot, "workshop", "description.bbcode");
        }

        if (options.ChangelogFile.Length == 0)
        {
            options.ChangelogFile = Path.Combine(repoRoot, "workshop", "changelog.txt");
        }

        options.Content = Path.GetFullPath(options.Content);
        options.Preview = Path.GetFullPath(options.Preview);
        options.TitleFile = Path.GetFullPath(options.TitleFile);
        options.DescriptionFile = Path.GetFullPath(options.DescriptionFile);
        options.ChangelogFile = Path.GetFullPath(options.ChangelogFile);
    }

    private static bool EnsureSteamAppIdFile(Reporter report)
    {
        string path = Path.Combine(Environment.CurrentDirectory, "steam_appid.txt");
        string expected = GameAppId.ToString(CultureInfo.InvariantCulture);
        try
        {
            if (File.Exists(path))
            {
                string existing = File.ReadAllText(path).Trim();
                if (existing == expected)
                {
                    return true;
                }

                report.Bad($"steam_appid.txt holds '{existing}', expected '{expected}' ({path})");
                return false;
            }

            File.WriteAllText(path, expected + Environment.NewLine, new UTF8Encoding(false));
            Log("wrote " + path);
            return true;
        }
        catch (IOException ex)
        {
            report.Bad($"cannot prepare steam_appid.txt: {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            report.Bad($"cannot prepare steam_appid.txt: {ex.Message}");
            return false;
        }
    }

    private static string? LoadText(string path, string label, Reporter report)
    {
        if (!File.Exists(path))
        {
            report.Missing($"{label} file {path}");
            return null;
        }

        try
        {
            string text = File.ReadAllText(path, Encoding.UTF8);
            Log($"{label} {text.Trim().Length} chars {Encoding.UTF8.GetByteCount(text)} bytes ({path})");
            return text;
        }
        catch (IOException ex)
        {
            report.Bad($"{label} file {path} is unreadable: {ex.Message}");
            return null;
        }
    }

    private static void CheckTitle(string? title, Reporter report)
    {
        if (title is null)
        {
            return;
        }

        int length = title.Trim().Length;
        if (length == 0)
        {
            report.Bad("title is empty");
            return;
        }

        if (length > MaxTitleChars)
        {
            report.Bad($"title is {length} chars (> {MaxTitleChars})");
        }
    }

    private static void CheckDescription(string? description, Reporter report)
    {
        if (description is null)
        {
            return;
        }

        int length = description.Trim().Length;
        if (length == 0)
        {
            report.Bad("description is empty");
            return;
        }

        if (length > MaxDescriptionChars)
        {
            report.Bad($"description is {length} chars (> {MaxDescriptionChars})");
        }

        // Steam enforces the limit in UTF-8 bytes, not characters: 5541 characters of Chinese is 7982 bytes and
        // passes, while 5942 characters (8704 bytes) came back as k_EResultInvalidParam at submit time.
        int bytes = Encoding.UTF8.GetByteCount(description.Trim());
        if (bytes > MaxDescriptionChars)
        {
            report.Bad($"description is {bytes} UTF-8 bytes (> {MaxDescriptionChars}); Steam would reject it with k_EResultInvalidParam");
        }
    }

    private static void CheckPreview(string preview, Reporter report)
    {
        if (!File.Exists(preview))
        {
            report.Missing("preview " + preview);
            return;
        }

        long bytes = new FileInfo(preview).Length;
        Log($"preview {Path.GetFileName(preview)} {bytes} bytes");
        if (bytes == 0)
        {
            report.Bad($"preview {preview} is empty");
        }
        else if (bytes > MaxPreviewBytes)
        {
            report.Bad($"preview is {bytes} bytes (> 1 MiB)");
        }
    }

    private static void CheckContent(string contentDir, Reporter report)
    {
        if (!Directory.Exists(contentDir))
        {
            report.Missing("content dir " + contentDir);
            return;
        }

        List<ContentFile> files = Directory.EnumerateFiles(contentDir, "*", SearchOption.AllDirectories)
            .Select(path => new ContentFile(Path.GetRelativePath(contentDir, path).Replace('\\', '/'), new FileInfo(path).Length))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToList();

        Log($"content {files.Count} files {files.Sum(file => file.Bytes)} bytes");

        foreach (ContentFile file in files.Take(FileListLimit))
        {
            Log($"file {file.RelativePath} {file.Bytes} bytes");
        }

        if (files.Count > FileListLimit)
        {
            Log($"file list truncated: {files.Count - FileListLimit} more files");
        }

        foreach (ContentFile file in files)
        {
            string relativePath = file.RelativePath;
            if (relativePath.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) || relativePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            {
                report.Bad($"content contains {relativePath} (pdb/zip must not ship)");
            }

            string[] segments = relativePath.Split('/');
            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (segments[i].Equals("obj", StringComparison.OrdinalIgnoreCase) || segments[i].Equals("bin", StringComparison.OrdinalIgnoreCase))
                {
                    report.Bad($"content contains {relativePath} (obj/bin must not ship)");
                    break;
                }
            }
        }

        string dll = Path.Combine(contentDir, ModId, ModId + ".dll");
        if (!File.Exists(dll))
        {
            report.Missing($"content {ModId}/{ModId}.dll");
        }
        else
        {
            long bytes = new FileInfo(dll).Length;
            if (bytes < MinModDllBytes)
            {
                report.Bad($"content {ModId}/{ModId}.dll is {bytes} bytes (< {MinModDllBytes})");
            }
        }

        string json = Path.Combine(contentDir, ModId, ModId + ".json");
        if (!File.Exists(json))
        {
            report.Missing($"content {ModId}/{ModId}.json");
            return;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(json, Encoding.UTF8));
            if (!document.RootElement.TryGetProperty("id", out JsonElement id) || id.ValueKind != JsonValueKind.String || id.GetString() != ModId)
            {
                report.Bad($"content {ModId}/{ModId}.json has no \"id\": \"{ModId}\"");
            }
        }
        catch (JsonException ex)
        {
            report.Bad($"content {ModId}/{ModId}.json is not valid JSON: {ex.Message}");
        }
    }

    /// <summary>
    /// The page spells the mod version out by hand, so it keeps advertising the old one after a bump
    /// (0.3.1-beta shipped while the page still said 0.3.0-beta). Catch it here instead of on the page.
    /// Only the mod version is checked: the game version sits in prose ("Slay the Spire 2 v0.113.1")
    /// where a token-based comparison would guess wrong.
    /// </summary>
    private static void CheckVersionMentions(string contentDir, string? description, Reporter report)
    {
        if (description is null)
        {
            return;
        }

        string manifest = Path.Combine(contentDir, ModId, ModId + ".json");
        if (!File.Exists(manifest))
        {
            return;
        }

        string? version;
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifest, Encoding.UTF8));
            version = document.RootElement.TryGetProperty("version", out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            report.Bad("manifest has no \"version\"");
            return;
        }

        MatchCollection mentions = Regex.Matches(description, @"(?:模组版本|mod version)\s*[:：]?\s*(v?\d+\.\d+\.\d+(?:-[A-Za-z0-9.]+)?)", RegexOptions.IgnoreCase);
        foreach (Match mention in mentions)
        {
            string mentioned = mention.Groups[1].Value;
            if (mentioned.TrimStart('v') != version)
            {
                report.Bad($"description says mod version {mentioned} but the manifest says {version}");
                return;
            }
        }
    }

    private static void CheckBranchVersions(Options options, Reporter report)
    {
        bool hasMin = options.BranchMin.Length > 0;
        bool hasMax = options.BranchMax.Length > 0;
        if (hasMin != hasMax)
        {
            report.Bad("--branch-min and --branch-max must be given together");
            return;
        }

        if (hasMin)
        {
            Log($"required game versions {options.BranchMin} .. {options.BranchMax}");
        }
    }

    private static int RunCreate(Options options, string repoRoot, string title, string description, string changelog)
    {
        // SteamAPI.Init only opens the pipe; a few frames drain the handshake so the first
        // Workshop call is not issued while the client is still settling.
        PumpCallbacks();

        SteamAPICall_t call = SteamUGC.CreateItem(new AppId_t(GameAppId), EWorkshopFileType.k_EWorkshopFileTypeCommunity);
        Log($"CreateItem call={call.m_SteamAPICall}");

        if (!WaitForCallResult(call, CreateTimeoutMs, out CreateItemResult_t created, out bool ioFailure, out bool timedOut))
        {
            Log(timedOut ? "BAD CreateItem timed out after 60s" : "BAD CreateItem was not delivered");
            return timedOut ? 2 : 1;
        }

        if (ioFailure)
        {
            Log("BAD CreateItem reported an I/O failure");
        }

        ulong publishedFileId = created.m_nPublishedFileId.m_PublishedFileId;
        Log($"CreateItem result={ResultName(created.m_eResult)} id={publishedFileId} needsLegalAgreement={created.m_bUserNeedsToAcceptWorkshopLegalAgreement}");
        if (created.m_bUserNeedsToAcceptWorkshopLegalAgreement)
        {
            Log("BAD accept the Steam Workshop legal agreement in the Steam client first");
        }

        if (created.m_eResult != EResult.k_EResultOK || publishedFileId == 0)
        {
            ExplainResult(created.m_eResult);
            return 1;
        }

        string itemIdPath = Path.Combine(repoRoot, "dist", "workshop-item-id.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(itemIdPath) ?? repoRoot);
        File.WriteAllText(itemIdPath, publishedFileId.ToString(CultureInfo.InvariantCulture) + Environment.NewLine, new UTF8Encoding(false));
        Log("wrote " + itemIdPath);

        int code = SubmitItem(publishedFileId, options, title, description, changelog);
        if (code != 0)
        {
            Log("the item exists but its upload failed; re-run with --update " + publishedFileId);
            return code;
        }

        return 0;
    }

    private static int RunUpdate(Options options, string title, string description, string changelog)
        => SubmitItem(options.PublishedFileId, options, title, description, changelog);

    private static int SubmitItem(ulong publishedFileId, Options options, string title, string description, string changelog)
    {
        PumpCallbacks();
        UGCUpdateHandle_t handle = SteamUGC.StartItemUpdate(new AppId_t(GameAppId), new PublishedFileId_t(publishedFileId));
        if (handle.m_UGCUpdateHandle == 0)
        {
            Log("BAD StartItemUpdate returned an invalid handle");
            return 1;
        }

        Log($"StartItemUpdate handle={handle.m_UGCUpdateHandle} id={publishedFileId}");

        bool ok = true;
        ok &= Must("SetItemTitle", SteamUGC.SetItemTitle(handle, title));
        ok &= Must("SetItemDescription", SteamUGC.SetItemDescription(handle, description));
        ok &= Must("SetItemContent", SteamUGC.SetItemContent(handle, options.Content));
        ok &= Must("SetItemPreview", SteamUGC.SetItemPreview(handle, options.Preview));
        ok &= Must("SetItemVisibility", SteamUGC.SetItemVisibility(handle, ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic));

        if (options.BranchMin.Length > 0)
        {
            ok &= Must("SetRequiredGameVersions", SteamUGC.SetRequiredGameVersions(handle, options.BranchMin, options.BranchMax));
        }

        if (!ok)
        {
            Log("BAD the item update handle is incomplete, nothing was submitted");
            return 1;
        }

        SteamAPICall_t call = SteamUGC.SubmitItemUpdate(handle, changelog);
        Log($"SubmitItemUpdate call={call.m_SteamAPICall} changelog={changelog}");

        int lastPercent = -1;
        bool finished = WaitForCallResult(
            call,
            SubmitTimeoutMs,
            out SubmitItemUpdateResult_t result,
            out bool ioFailure,
            out bool timedOut,
            () =>
            {
                EItemUpdateStatus status = SteamUGC.GetItemUpdateProgress(handle, out ulong processed, out ulong total);
                int percent = total > 0 ? (int)(processed * 100UL / total) : 0;
                if (percent != lastPercent)
                {
                    lastPercent = percent;
                    Log($"upload {percent}% ({StatusName(status)})");
                }
            });

        if (!finished)
        {
            Log(timedOut ? "BAD SubmitItemUpdate timed out" : "BAD SubmitItemUpdate was not delivered");
            return timedOut ? 2 : 1;
        }

        if (ioFailure)
        {
            Log("BAD SubmitItemUpdate reported an I/O failure");
        }

        Log("EResult " + ResultName(result.m_eResult));
        if (result.m_bUserNeedsToAcceptWorkshopLegalAgreement)
        {
            Log("BAD accept the Steam Workshop legal agreement in the Steam client first");
        }

        if (result.m_eResult != EResult.k_EResultOK)
        {
            ExplainResult(result.m_eResult);
            return 1;
        }

        ulong finalId = result.m_nPublishedFileId.m_PublishedFileId != 0 ? result.m_nPublishedFileId.m_PublishedFileId : publishedFileId;
        Log($"published {finalId} as public");
        return 0;
    }

    private static bool Must(string name, bool value)
    {
        if (!value)
        {
            Log($"BAD {name} returned false");
        }

        return value;
    }

    // Call results arrive only through CallResult<T>: this Steamworks.NET build runs the manual
    // dispatch pipe (CallbackDispatcher.Initialize -> SteamAPI_ManualDispatch_Init; RunFrame takes
    // callback id 703 = SteamAPICallCompleted_t and dispatches by API call handle), so neither
    // IsAPICallCompleted nor a broadcast Callback<T> can observe CreateItemResult_t.
    private static bool WaitForCallResult<T>(
        SteamAPICall_t call,
        int timeoutMs,
        out T result,
        out bool ioFailure,
        out bool timedOut,
        Action? onTick = null)
        where T : struct
    {
        result = default;
        ioFailure = false;
        timedOut = false;

        T payload = default;
        bool failed = false;
        bool fired = false;
        using CallResult<T> registration = CallResult<T>.Create((T value, bool bIOFailure) =>
        {
            payload = value;
            failed = bIOFailure;
            fired = true;
        });
        registration.Set(call);

        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < timeoutMs)
        {
            SteamAPI.RunCallbacks();
            onTick?.Invoke();

            if (fired)
            {
                result = payload;
                ioFailure = failed;
                return true;
            }

            Thread.Sleep(50);
        }

        timedOut = true;
        Log($"BAD no call result for call={call.m_SteamAPICall} within {timeoutMs} ms");
        return false;
    }

    private static string FailureReason(SteamAPICall_t call)
    {
        ESteamAPICallFailure failure = SteamUtils.GetAPICallFailureReason(call);
        return Enum.GetName(failure) ?? failure.ToString();
    }

    private static void PumpCallbacks(int iterations = 10, int delayMs = 50)
    {
        for (int i = 0; i < iterations; i++)
        {
            SteamAPI.RunCallbacks();
            Thread.Sleep(delayMs);
        }
    }

    private static void ExplainResult(EResult result)
    {
        switch (result)
        {
            case EResult.k_EResultOK:
                return;
            case EResult.k_EResultAccessDenied:
            case EResult.k_EResultInsufficientPrivilege:
                Log("BAD this account cannot publish for this app: the Workshop is not open for app 2868840, or the account is not allowed to publish for it");
                break;
            case EResult.k_EResultInvalidState:
                Log("BAD the Steam client refuses Workshop writes in its current state (k_EResultInvalidState)");
                break;
            case EResult.k_EResultNotLoggedOn:
            case EResult.k_EResultNoConnection:
            case EResult.k_EResultTimeout:
                Log("BAD the Steam client is offline or not logged on; sign in, let the client finish syncing, then re-run");
                break;
            case EResult.k_EResultLimitExceeded:
            case EResult.k_EResultCommunityCooldown:
                Log("BAD this app's Workshop limits creation for this account; wait for the cooldown and re-run");
                break;
            case EResult.k_EResultBanned:
            case EResult.k_EResultBlocked:
            case EResult.k_EResultServiceReadOnly:
                Log("BAD Steam refuses this account's Workshop writes (banned, blocked or read-only service)");
                break;
            case EResult.k_EResultMustAgreeToSSA:
                Log("BAD accept the Steam Subscriber Agreement in the Steam client first");
                break;
            default:
                Log("BAD the Workshop rejected the request; the EResult above names the reason");
                break;
        }
    }

    private static int RunProbe()
    {
        // Read-only: no CreateItem, no StartItemUpdate, no SubmitItemUpdate, no DeleteItem.
        Log("probe: read-only, nothing is created or changed");
        Log($"probe steamid={SteamUser.GetSteamID().m_SteamID} appid={SteamUtils.GetAppID().m_AppId} persona={SteamFriends.GetPersonaName()}");
        Log($"probe SteamApps.BIsSubscribed()={SteamApps.BIsSubscribed()}");
        Log($"probe SteamApps.BIsSubscribedApp(2868840)={SteamApps.BIsSubscribedApp(new AppId_t(GameAppId))}");
        Log($"probe SteamUGC.GetNumSubscribedItems()={SteamUGC.GetNumSubscribedItems()}");

        Assembly assembly = typeof(SteamUtils).Assembly;
        // GetTypes() throws on this assembly: CoreCLR cannot load its OptionValue struct, so the
        // one generic type is resolved by name instead of enumerating the module.
        try
        {
            Log(DescribeCallbackType(assembly.GetType("Steamworks.Callback\u00601")));
        }
        catch (Exception ex)
        {
            Log("probe reflection generic Callback type unreadable: " + ex.GetType().Name + " " + ex.Message);
        }

        Log($"probe reflection CreateItemResult_t interfaces={string.Join(",", typeof(CreateItemResult_t).GetInterfaces().Select(i => i.Name))}");

        foreach (string name in new[] { "IsAPICallCompleted", "GetAPICallResult", "GetAPICallFailureReason" })
        {
            MethodInfo? method = typeof(SteamUtils).GetMethod(name);
            Log(method == null ? "probe reflection SteamUtils." + name + " missing" : "probe reflection SteamUtils." + DescribeMethod(method));
        }

        try
        {
            MethodInfo? native = assembly.GetType("Steamworks.NativeMethods")?.GetMethod(
                "SteamAPI_ISteamUtils_IsAPICallCompleted",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            Log(native == null ? "probe reflection native IsAPICallCompleted missing" : "probe reflection native " + DescribeMethod(native));
        }
        catch (Exception ex)
        {
            Log("probe reflection native lookup failed: " + ex.GetType().Name + " " + ex.Message);
        }

        for (int i = 0; i < 3; i++)
        {
            SteamAPICall_t bogus = new SteamAPICall_t(ulong.MaxValue - (ulong)i);
            bool completed = SteamUtils.IsAPICallCompleted(bogus, out bool flaggedFailed);
            Log($"probe bogus call={bogus.m_SteamAPICall} completed={completed} flaggedFailed={flaggedFailed} failureReason={FailureReason(bogus)}");
        }

        try
        {
            Type? owner = typeof(Callback<CreateItemResult_t>);
            Type? nested = owner.GetNestedType("DispatchDelegate", BindingFlags.Public | BindingFlags.NonPublic)
                ?? owner.BaseType?.GetNestedType("DispatchDelegate", BindingFlags.Public | BindingFlags.NonPublic);
            if (nested == null)
            {
                Log("probe reflection dispatch delegate missing; nested=" + string.Join(",", owner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).Select(t => t.Name)));
            }
            else
            {
                MethodInfo? invoke = nested.GetMethod("Invoke");
                Log(invoke == null ? "probe reflection dispatch delegate has no Invoke" : "probe reflection dispatch " + DescribeMethod(invoke));
            }
        }
        catch (Exception ex)
        {
            Log("probe reflection dispatch delegate unreadable: " + ex.GetType().Name + " " + ex.Message);
        }

        try
        {
            MethodInfo? request = typeof(SteamMatchmaking).GetMethod("RequestLobbyList", Type.EmptyTypes);
            if (request == null)
            {
                Log("probe SteamMatchmaking.RequestLobbyList() missing, call-result experiment skipped");
            }
            else if (request.Invoke(null, null) is SteamAPICall_t probeCall)
            {
                Log($"probe RequestLobbyList call={probeCall.m_SteamAPICall}");
                bool delivered = WaitForCallResult(probeCall, 10000, out LobbyMatchList_t lobbies, out bool ioFailure, out bool timedOut);
                Log(delivered
                    ? $"probe RequestLobbyList delivered lobbies={lobbies.m_nLobbiesMatching} ioFailure={ioFailure}"
                    : $"probe RequestLobbyList delivered=False timedOut={timedOut}");
            }
            else
            {
                Log("probe RequestLobbyList returned nothing usable");
            }
        }
        catch (Exception ex)
        {
            Log("probe RequestLobbyList failed: " + ex.GetType().Name + " " + ex.Message);
        }

        Log("probe done");
        return 0;
    }

    private static string DescribeMethod(MethodInfo method)
    {
        string parameters = string.Join(", ", method.GetParameters().Select(p => (p.IsOut ? "out " : string.Empty) + p.ParameterType.Name + " " + p.Name));
        string marshal = method.ReturnParameter.GetCustomAttribute<MarshalAsAttribute>() is { } attribute
            ? " returnMarshalAs=" + attribute.Value
            : string.Empty;
        return $"{method.DeclaringType?.Name}.{method.Name}({parameters}) -> {method.ReturnType.Name}{marshal}";
    }

    private static string DescribeCallbackType(Type? type)
    {
        if (type == null)
        {
            return "probe reflection generic Callback type missing";
        }

        Type argument = type.GetGenericArguments()[0];
        string constraints = string.Join("; ", argument.GetGenericParameterConstraints().Select(c => c.Name));
        string methods = string.Join(" | ", type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(m => m.Name + "(" + string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)) + ") -> " + m.ReturnType.Name));
        return $"probe reflection {type.Name} base={type.BaseType?.Name} constraint={argument.Name}:{constraints} static={methods}";
    }

    private static string ResultName(EResult result) => Enum.GetName(result) ?? result.ToString();

    private static string StatusName(EItemUpdateStatus status) => status switch
    {
        EItemUpdateStatus.k_EItemUpdateStatusPreparingConfig => "preparing config",
        EItemUpdateStatus.k_EItemUpdateStatusPreparingContent => "preparing content",
        EItemUpdateStatus.k_EItemUpdateStatusUploadingContent => "uploading content",
        EItemUpdateStatus.k_EItemUpdateStatusUploadingPreviewFile => "uploading preview",
        EItemUpdateStatus.k_EItemUpdateStatusCommittingChanges => "committing changes",
        _ => "invalid",
    };

    private static string ModeName(Mode mode) => mode switch
    {
        Mode.DryRun => "dry-run",
        Mode.Probe => "probe",
        Mode.Create => "create",
        Mode.Update => "update",
        _ => "unknown",
    };

    private static void Log(string message) => Console.WriteLine("[workshop] " + message);
}
