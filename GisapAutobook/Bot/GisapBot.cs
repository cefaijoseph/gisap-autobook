using System.Text.Json;
using Microsoft.Playwright;

namespace GisapAutobook.Bot;

public class GisapBot
{
    private readonly IConfiguration _config;
    private readonly ILogger<GisapBot> _logger;
    private DateTime? _addToCartClickedAt;

    private const string GisapBaseUrl = "https://gisap.gov.mt";

    public GisapBot(IConfiguration config, ILogger<GisapBot> logger)
    {
        _config = config;
        _logger = logger;
    }

    public async Task<BookingResult> BookAsync(BookingRequest request, CancellationToken ct = default)
    {
        var storageStatePath = ResolvePath(_config["Bot:StorageStatePath"] ?? Path.Combine("bot-session", "storage-state.json"));
        var screenshotsDir = ResolvePath(_config["Bot:ScreenshotsPath"] ?? "bot-screenshots");
        var headless = _config.GetValue("Bot:Headless", true);
        var dryRun = _config.GetValue("Bot:DryRun", false);
        var slowMo = _config.GetValue("Bot:SlowMo", 0);

        if (dryRun)
            _logger.LogWarning("[DRY RUN] Checkout step will be skipped — no real booking will be made.");

        Directory.CreateDirectory(Path.GetDirectoryName(storageStatePath)!);
        Directory.CreateDirectory(screenshotsDir);

        using var playwright = await Playwright.CreateAsync();
        var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = headless,
            SlowMo = slowMo,
            Channel = "chrome",
            Args = new[]
            {
                "--disable-blink-features=AutomationControlled",
                "--disable-features=IsolateOrigins,site-per-process",
                "--no-sandbox",
                "--disable-dev-shm-usage"
            }
        });

        IBrowserContext context;
        var contextOptions = new BrowserNewContextOptions
        {
            UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36",
            ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
            Locale = "en-US",
            TimezoneId = "Europe/Malta"
        };
        if (File.Exists(storageStatePath))
        {
            _logger.LogInformation("Loading saved browser session from {Path}", storageStatePath);
            contextOptions.StorageStatePath = storageStatePath;
        }
        context = await browser.NewContextAsync(contextOptions);

        // Mask navigator.webdriver to avoid Cloudflare bot detection
        await context.AddInitScriptAsync("Object.defineProperty(navigator, 'webdriver', { get: () => undefined });");

        var page = await context.NewPageAsync();

        try
        {
            await CheckAndLoginAsync(page, context, storageStatePath);
            await NavigateAndBookAsync(page, request, dryRun, ct);
            return BookingResult.Success(_addToCartClickedAt ?? DateTime.UtcNow);
        }
        catch (SlotUnavailableException)
        {
            _logger.LogWarning("Schedule {ScheduleId}: slot already taken, stopping without retry", request.ScheduleId);
            return BookingResult.AlreadyBooked(_addToCartClickedAt ?? DateTime.UtcNow);
        }
        catch (SlotNotOpenYetException)
        {
            _logger.LogWarning("Schedule {ScheduleId}: booking window not open yet", request.ScheduleId);
            return BookingResult.NotOpenYet();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Booking failed for schedule {ScheduleId}", request.ScheduleId);
            var screenshotPath = Path.Combine(screenshotsDir,
                $"{request.ScheduleId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png");
            try
            {
                await page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshotPath, FullPage = true });
                _logger.LogInformation("Screenshot saved to {Path}", screenshotPath);
            }
            catch (Exception screenshotEx)
            {
                _logger.LogWarning(screenshotEx, "Could not save failure screenshot");
            }

            return BookingResult.Failure(ex.Message, screenshotPath);
        }
        finally
        {
            await context.CloseAsync();
            await browser.CloseAsync();
        }
    }

    private async Task CheckAndLoginAsync(IPage page, IBrowserContext context, string storageStatePath)
    {
        _logger.LogInformation("Navigating to GISAP home page");
        await page.GotoAsync(GisapBaseUrl, new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30000 });

        // Open hamburger menu if present (mobile/responsive layout hides nav items behind it)
        var hamburger = page.Locator("[aria-label='Toggle Menu']");
        if (await hamburger.IsVisibleAsync())
        {
            _logger.LogInformation("Hamburger menu detected — opening it");
            await hamburger.ClickAsync();
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        }

        var myAccountVisible = await page.Locator("a:has-text('My Account'), a:has-text('my account')").CountAsync() > 0;

        if (myAccountVisible)
        {
            _logger.LogInformation("Already logged in (My Account detected)");
            return;
        }

        var loginVisible = await page.Locator("a:has-text('Login'), button:has-text('Login')").CountAsync() > 0;

        if (!loginVisible)
        {
            _logger.LogWarning("Neither Login nor My Account found on page — proceeding anyway");
            return;
        }

        _logger.LogInformation("Login required — starting authentication flow");
        await PerformLoginAsync(page, context, storageStatePath);
    }

    private async Task PerformLoginAsync(IPage page, IBrowserContext context, string storageStatePath)
    {
        var email = _config["Google:Email"]
            ?? throw new InvalidOperationException("Google:Email is not configured. Use dotnet user-secrets or environment variables.");
        var password = _config["Google:Password"]
            ?? throw new InvalidOperationException("Google:Password is not configured. Use dotnet user-secrets or environment variables.");

        // Click the Login link/button
        await page.ClickAsync("a:has-text('Login'), button:has-text('Login')");

        // Wait for the modal to appear with the e-ID / Social Account button
        _logger.LogInformation("Waiting for login modal");
        await page.WaitForSelectorAsync("text=e-ID or Social Account",
            new PageWaitForSelectorOptions { Timeout = 15000 });
        await page.ClickAsync("text=e-ID or Social Account");

        // After clicking e-ID, the site may either show the Google button OR log in directly.
        // Wait up to 5s for either outcome before deciding which path to take.
        _logger.LogInformation("Waiting for Google button or direct login");
        await page.WaitForTimeoutAsync(2000);

        // Open hamburger if needed (could be on any viewport after redirect)
        var hamburgerCheck = page.Locator("[aria-label='Toggle Menu']");
        if (await hamburgerCheck.IsVisibleAsync())
            await hamburgerCheck.ClickAsync();

        if (await page.Locator("a:has-text('My Account'), a:has-text('my account')").CountAsync() > 0)
        {
            // e-ID clicked and site auto-logged us in — session already active
            _logger.LogInformation("Auto-logged in after e-ID click — skipping Google OAuth");
            await context.StorageStateAsync(new BrowserContextStorageStateOptions { Path = storageStatePath });
            _logger.LogInformation("Session state saved to {Path}", storageStatePath);
            return;
        }

        // Click the Google button that appears after the e-ID modal
        await page.WaitForSelectorAsync("text=Google",
            new PageWaitForSelectorOptions { Timeout = 15000 });
        await page.ClickAsync("text=Google");

        // After clicking Google, the site may auto-login back to GISAP or go to OAuth
        _logger.LogInformation("Waiting for redirect after Google click");
        await page.WaitForURLAsync(
            new System.Text.RegularExpressions.Regex(@"accounts\.google\.com|gisap\.gov\.mt"),
            new PageWaitForURLOptions { Timeout = 20000 });

        var hamburgerCheck2 = page.Locator("[aria-label='Toggle Menu']");
        if (await hamburgerCheck2.IsVisibleAsync())
            await hamburgerCheck2.ClickAsync();

        if (await page.Locator("a:has-text('My Account'), a:has-text('my account')").CountAsync() > 0)
        {
            _logger.LogInformation("Auto-logged in after Google click — skipping credentials");
            await context.StorageStateAsync(new BrowserContextStorageStateOptions { Path = storageStatePath });
            _logger.LogInformation("Session state saved to {Path}", storageStatePath);
            return;
        }

        // Enter email
        await page.WaitForSelectorAsync("input[type='email']", new PageWaitForSelectorOptions { Timeout = 15000 });
        await page.FillAsync("input[type='email']", email);
        await page.ClickAsync("#identifierNext, button:has-text('Next')");

        // Enter password
        await page.WaitForSelectorAsync("input[type='password']:visible",
            new PageWaitForSelectorOptions { Timeout = 15000 });
        await page.FillAsync("input[type='password']", password);
        await page.ClickAsync("#passwordNext, button:has-text('Next')");

        // Wait for redirect back to GISAP after password / 2FA.
        // LoginTimeoutSeconds is long by default (120s) so a 2FA prompt on a visible
        // browser can be completed manually before the timeout expires.
        var loginTimeoutMs = _config.GetValue("Bot:LoginTimeoutSeconds", 120) * 1000;
        _logger.LogInformation("Waiting for redirect back to GISAP (timeout: {Sec}s) — complete any 2FA prompt in the browser",
            loginTimeoutMs / 1000);
        await page.WaitForURLAsync("**/gisap.gov.mt/**",
            new PageWaitForURLOptions { Timeout = loginTimeoutMs });

        // Persist the session so we do not need to log in on subsequent runs
        await context.StorageStateAsync(new BrowserContextStorageStateOptions
        {
            Path = storageStatePath
        });
        _logger.LogInformation("Session state saved to {Path}", storageStatePath);
    }

    private async Task NavigateAndBookAsync(IPage page, BookingRequest request, bool dryRun, CancellationToken ct = default)
    {
        var reservationUrl =
            $"{GisapBaseUrl}/reservations/?resource_id={request.ResourceId}&mode=reserve&planyo_lang=EN";

        // Compute the booking window open time up-front
        var maltaTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Malta");
        var slotLocalDt = DateTime.SpecifyKind(request.BookingDate.Date.AddHours(request.StartHour), DateTimeKind.Unspecified);
        var slotUtc = TimeZoneInfo.ConvertTimeToUtc(slotLocalDt, maltaTz);
        var windowOpensUtc = slotUtc.AddDays(-14);

        int retryCount = _config.GetValue("Bot:RetryCount", 9);
        int maxAttempts = retryCount + 1;

        // Navigate and fill form
        await NavigateAndFillFormAsync(page, request, reservationUrl);

        // Coarse wait — Task.Delay until 50ms before window opens
        var coarseWait = windowOpensUtc - DateTime.UtcNow - TimeSpan.FromMilliseconds(50);
        if (coarseWait > TimeSpan.Zero)
        {
            _logger.LogInformation("Form ready. Waiting until booking window opens at {Time:HH:mm:ss.fff} UTC",
                windowOpensUtc);
            await Task.Delay(coarseWait, ct);
        }

        // Spin-wait the last ≤50ms for tight precision
        while (DateTime.UtcNow < windowOpensUtc)
            ct.ThrowIfCancellationRequested();

        // --- Add to cart (with retry + re-navigation if window not open yet) ---
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            _addToCartClickedAt = DateTime.UtcNow;
            await page.ClickAsync(
                "input[value*='Add to cart'], button:has-text('Add to cart'), a:has-text('Add to cart')");
            await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

            // Wait briefly for #res_error_msg to be populated
            try
            {
                await page.WaitForFunctionAsync(
                    "() => (document.querySelector('#res_error_msg')?.textContent?.trim().length ?? 0) > 0",
                    null,
                    new PageWaitForFunctionOptions { Timeout = 3000 });
            }
            catch (TimeoutException)
            {
                // No error — booking likely succeeded
            }

            var errorMsg = "";
            var errorDiv = page.Locator("#res_error_msg");
            if (await errorDiv.CountAsync() > 0)
                errorMsg = (await errorDiv.InnerTextAsync()).Trim();

            if (errorMsg.Contains("Cannot rent resource", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Slot is already taken — 'Cannot rent resource' in #res_error_msg");
                throw new SlotUnavailableException();
            }

            if (errorMsg.Contains("It is too soon to make this reservation", StringComparison.OrdinalIgnoreCase))
            {
                if (attempt < maxAttempts)
                {
                    _logger.LogWarning("Attempt {Attempt}/{Max}: booking window not open yet — re-navigating and retrying",
                        attempt, maxAttempts);
                    await NavigateAndFillFormAsync(page, request, reservationUrl);
                    continue;
                }
                _logger.LogWarning("Booking window not open yet after {Max} attempts — giving up", maxAttempts);
                throw new SlotNotOpenYetException();
            }

            // No error — Add to Cart succeeded
            break;
        }

        // --- Proceed to checkout (skipped in dry-run mode) ---
        if (dryRun)
        {
            _logger.LogWarning("[DRY RUN] Stopping before checkout — item is in cart but NOT confirmed.");
            var pauseSec = _config.GetValue("Bot:DryRunPauseSeconds", 60);
            _logger.LogWarning("[DRY RUN] Browser will stay open for {Sec}s so you can inspect the cart. Close it manually or wait.", pauseSec);
            await page.WaitForTimeoutAsync(pauseSec * 1000);
            return;
        }

        _logger.LogInformation("Proceeding to checkout");
        await page.ClickAsync(
            "a:has-text('Proceed to checkout'), button:has-text('Proceed to checkout'), " +
            "a:has-text('checkout'), input[value*='checkout']");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        _logger.LogInformation("Booking completed successfully for schedule {ScheduleId}", request.ScheduleId);
    }

    private async Task NavigateAndFillFormAsync(IPage page, BookingRequest request, string reservationUrl)
    {
        _logger.LogInformation("Navigating to reservation page {Url}", reservationUrl);
        await page.GotoAsync(reservationUrl, new PageGotoOptions { WaitUntil = WaitUntilState.Load, Timeout = 30000 });

        var dateStr = request.BookingDate.ToString("yyyy-MM-dd");
        _logger.LogInformation("Setting booking date to {Date}", dateStr);

        var dateLocator = page.Locator("#one_date");
        await dateLocator.WaitForAsync(new LocatorWaitForOptions { Timeout = 15000 });
        await dateLocator.ClickAsync(new LocatorClickOptions { ClickCount = 3 });
        await dateLocator.FillAsync(dateStr);
        await dateLocator.EvaluateAsync("el => el.dispatchEvent(new Event('change', { bubbles: true }))");

        _logger.LogInformation("Setting number of persons to 4");
        await page.SelectOptionAsync("#rental_prop_persons", "4");
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        _logger.LogInformation("Setting start time to {Hour}:00", request.StartHour);
        await page.SelectOptionAsync("#start_time", request.StartHour.ToString());
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        _logger.LogInformation("Setting end time to {Hour}:00", request.EndHour);
        await page.SelectOptionAsync("#end_time", request.EndHour.ToString());
        await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);

        _logger.LogInformation("Accepting Terms & Conditions");
        await page.CheckAsync("#rental_prop_I_agree_with_GISAP_Terms_and_Conditions");
    }

    private static string ResolvePath(string relativePath) =>
        Path.IsPathRooted(relativePath)
            ? relativePath
            : Path.Combine(AppContext.BaseDirectory, relativePath);

    public static string ResolveSessionPath(IConfiguration config) =>
        ResolvePath(config["Bot:StorageStatePath"] ?? Path.Combine("bot-session", "storage-state.json"));

    /// <summary>
    /// Returns the earliest expiry of Google auth cookies in the saved session,
    /// or null if the file doesn't exist / can't be parsed.
    /// </summary>
    public static DateTime? ParseSessionExpiry(IConfiguration config)
    {
        var path = ResolveSessionPath(config);
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("cookies", out var cookies)) return null;

            DateTime? earliest = null;
            foreach (var cookie in cookies.EnumerateArray())
            {
                if (!cookie.TryGetProperty("domain", out var domainEl)) continue;
                var domain = domainEl.GetString() ?? "";
                if (!domain.Contains("google.com")) continue;

                if (!cookie.TryGetProperty("expires", out var expiresEl)) continue;
                var expiresUnix = expiresEl.GetDouble();
                if (expiresUnix <= 0) continue; // session-only cookie

                var expiry = DateTimeOffset.FromUnixTimeSeconds((long)expiresUnix).UtcDateTime;
                if (earliest == null || expiry < earliest)
                    earliest = expiry;
            }
            return earliest;
        }
        catch { return null; }
    }
}

public class SlotUnavailableException : Exception
{
    public SlotUnavailableException() : base("Slot already booked by someone else") { }
}

public class SlotNotOpenYetException : Exception
{
    public SlotNotOpenYetException() : base("Booking window not open yet") { }
}
